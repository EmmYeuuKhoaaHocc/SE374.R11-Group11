const express = require('express');
const crypto = require('crypto');
const config = require('../config');
const store = require('../store');
const keys = require('../keys');
const { renderLoginPage } = require('../views/renderLogin');

const router = express.Router();

function isValidPkceParams(codeChallenge, codeChallengeMethod) {
  return (
    typeof codeChallenge === 'string' &&
    codeChallenge.trim().length >= 43 &&
    codeChallengeMethod === 'S256'
  );
}

function parseAuthorizeRequest(source) {
  const codeChallenge = source.code_challenge;
  const codeChallengeMethod = source.code_challenge_method;

  if (!isValidPkceParams(codeChallenge, codeChallengeMethod)) {
    return {
      error: {
        error: 'invalid_request',
        error_description: 'PKCE code_challenge and code_challenge_method=S256 are required'
      }
    };
  }

  return {
    params: {
      clientId: source.client_id || config.clientId,
      redirectUri: source.redirect_uri || config.defaultRedirectUri,
      state: source.state || '',
      scope: source.scope || 'openid profile cccd bhyt',
      nonce: source.nonce || '',
      codeChallenge,
      codeChallengeMethod
    }
  };
}

router.get('/oauth/authorize', (req, res) => {
  const parsed = parseAuthorizeRequest(req.query);
  if (parsed.error) {
    return res.status(400).json(parsed.error);
  }

  const html = renderLoginPage({
    ...parsed.params,
    citizens: store.getCitizens(),
    practitioners: store.getPractitioners(),
    facilities: store.getFacilities()
  });
  res.setHeader('Content-Type', 'text/html; charset=utf-8');
  return res.send(html);
});

router.post('/oauth/authorize', (req, res) => {
  const parsed = parseAuthorizeRequest(req.body);
  if (parsed.error) {
    return res.status(400).json(parsed.error);
  }

  const cccd = (req.body.cccd || '').trim();
  const citizen = store.findCitizen(cccd);
  if (!citizen) {
    const html = renderLoginPage({
      ...parsed.params,
      citizens: store.getCitizens(),
      practitioners: store.getPractitioners(),
      facilities: store.getFacilities(),
      error: `Số định danh ${cccd} không tồn tại trong CSDL dân cư mẫu.`
    });
    return res.status(400).send(html);
  }

  const record = store.createAuthCode({
    citizen,
    ...parsed.params
  });

  const redirectUrl = new URL(parsed.params.redirectUri);
  redirectUrl.searchParams.set('code', record.code);
  if (parsed.params.state) {
    redirectUrl.searchParams.set('state', parsed.params.state);
  }

  return res.redirect(302, redirectUrl.toString());
});

router.post('/oauth/token', async (req, res) => {
  const grantType = req.body.grant_type || req.query.grant_type;
  const code = req.body.code || req.query.code;
  const clientId = req.body.client_id || req.query.client_id;
  const clientSecret = req.body.client_secret || req.query.client_secret;
  const redirectUri = req.body.redirect_uri || req.query.redirect_uri;
  const codeVerifier = req.body.code_verifier || req.query.code_verifier;

  if (grantType !== 'authorization_code') {
    return res.status(400).json({
      error: 'unsupported_grant_type',
      error_description: 'Only authorization_code grant type is supported'
    });
  }

  if (!code) {
    return res.status(400).json({
      error: 'invalid_request',
      error_description: 'Missing code parameter'
    });
  }

  if (!codeVerifier) {
    return res.status(400).json({
      error: 'invalid_request',
      error_description: 'Missing PKCE code_verifier parameter'
    });
  }

  if (clientSecret && clientSecret !== config.clientSecret) {
    return res.status(401).json({
      error: 'invalid_client',
      error_description: 'Client authentication failed'
    });
  }

  const record = store.consumeAuthCode(code);
  if (!record) {
    return res.status(400).json({
      error: 'invalid_grant',
      error_description: 'Authorization code is invalid or has expired'
    });
  }

  if (clientId && clientId !== record.clientId) {
    return res.status(400).json({
      error: 'invalid_grant',
      error_description: 'client_id mismatch'
    });
  }

  if (!redirectUri || redirectUri !== record.redirectUri) {
    return res.status(400).json({
      error: 'invalid_grant',
      error_description: 'redirect_uri mismatch'
    });
  }

  if (!store.verifyPkce(record, codeVerifier)) {
    return res.status(400).json({
      error: 'invalid_grant',
      error_description: 'PKCE code_verifier verification failed'
    });
  }

  const citizen = record.citizen;
  const practitioner = store.findPractitionerByCccd(citizen.cccd);

  const baseClaims = {
    cccd: citizen.cccd,
    name: citizen.ho_va_ten,
    birthdate: citizen.ngay_sinh,
    gender: citizen.gioi_tinh,
    phone_number: citizen.so_dien_thoai,
    address: citizen.dia_chi_thuong_tru,
    bhyt_number: citizen.so_the_bhyt,
    bhyt_rate: citizen.muc_huong_bhyt,
    roles: practitioner ? ['Doctor', 'Citizen'] : ['Citizen']
  };

  if (practitioner) {
    baseClaims.cchn = practitioner.ma_cchn;
    baseClaims.facility_id = practitioner.ma_cskcb_cong_tac;
    baseClaims.scope_of_practice = practitioner.pham_vi_hoat_dong;
    baseClaims.practitioner = {
      cchn: practitioner.ma_cchn,
      facility_id: practitioner.ma_cskcb_cong_tac,
      scope_of_practice: practitioner.pham_vi_hoat_dong,
      status: practitioner.trang_thai
    };
  }

  if (record.nonce) {
    baseClaims.nonce = record.nonce;
  }

  const accessToken = keys.signJwt(
    {
      ...baseClaims,
      token_use: 'access_token',
      scope: record.scope
    },
    citizen.cccd,
    clientId || config.clientId,
    config.tokenExpiresIn
  );

  const idToken = keys.signJwt(
    {
      ...baseClaims,
      token_use: 'id_token'
    },
    citizen.cccd,
    clientId || config.clientId,
    config.tokenExpiresIn
  );

  const refreshToken = crypto.randomBytes(32).toString('hex');

  return res.json({
    access_token: accessToken,
    id_token: idToken,
    refresh_token: refreshToken,
    token_type: 'Bearer',
    expires_in: config.tokenExpiresIn
  });
});

router.get('/.well-known/jwks.json', (req, res) => {
  res.json(keys.getJwks());
});

router.get('/.well-known/openid-configuration', (req, res) => {
  res.json({
    issuer: config.issuer,
    authorization_endpoint: `${config.issuer}/oauth/authorize`,
    token_endpoint: `${config.issuer}/oauth/token`,
    jwks_uri: `${config.issuer}/.well-known/jwks.json`,
    response_types_supported: ['code'],
    subject_types_supported: ['public'],
    id_token_signing_alg_values_supported: ['RS256'],
    code_challenge_methods_supported: ['S256'],
    scopes_supported: ['openid', 'profile', 'cccd', 'bhyt']
  });
});

module.exports = router;

