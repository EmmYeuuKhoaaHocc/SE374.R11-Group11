const test = require('node:test');
const assert = require('node:assert/strict');
const crypto = require('node:crypto');
const app = require('../src/app');
const keys = require('../src/keys');
const store = require('../src/store');
const { renderLoginPage } = require('../src/views/renderLogin');

function createPkcePair() {
  const verifier = crypto.randomBytes(32).toString('base64url');
  const challenge = crypto.createHash('sha256').update(verifier, 'ascii').digest('base64url');
  return { verifier, challenge };
}

test('keys exports key-derived kid and verifiable RS256 JWT', () => {
  const jwks = keys.getJwks();
  assert.equal(jwks.keys.length, 1);
  const jwk = jwks.keys[0];
  assert.notEqual(jwk.kid, 'vneid-mock-key-1');
  assert.equal(jwk.alg, 'RS256');

  const token = keys.signJwt({ nonce: 'n-123', token_use: 'id_token' }, '001200099999', 'neuroscan-client', 3600);
  const [headerB64, payloadB64, sigB64] = token.split('.');
  const header = JSON.parse(Buffer.from(headerB64, 'base64url').toString('utf8'));
  const payload = JSON.parse(Buffer.from(payloadB64, 'base64url').toString('utf8'));
  assert.equal(header.kid, jwk.kid);
  assert.equal(payload.nonce, 'n-123');

  const pubKey = crypto.createPublicKey({ key: jwk, format: 'jwk' });
  const verifier = crypto.createVerify('RSA-SHA256');
  verifier.update(`${headerB64}.${payloadB64}`);
  assert.equal(verifier.verify(pubKey, Buffer.from(sigB64, 'base64url')), true);
});

test('store validates PKCE S256 strictly and consumes auth code once', () => {
  const { verifier, challenge } = createPkcePair();
  const citizen = store.findCitizen('001200099999');
  assert.ok(citizen);

  const record = store.createAuthCode({
    citizen,
    clientId: 'neuroscan-client',
    redirectUri: 'http://localhost:3000/auth/callback',
    scope: 'openid profile',
    state: 'st-1',
    nonce: 'nn-1',
    codeChallenge: challenge,
    codeChallengeMethod: 'S256'
  });

  assert.equal(store.verifyPkce(record, verifier), true);
  assert.equal(store.verifyPkce(record, `${verifier}x`), false);

  const consumed = store.consumeAuthCode(record.code);
  assert.ok(consumed);
  assert.equal(store.consumeAuthCode(record.code), null);
});

test('renderLoginPage includes hidden PKCE fields', () => {
  const html = renderLoginPage({
    clientId: 'neuroscan-client',
    redirectUri: 'http://localhost:3000/auth/callback',
    state: 'st-1',
    scope: 'openid',
    nonce: 'nn-1',
    codeChallenge: 'chal-123',
    codeChallengeMethod: 'S256',
    citizens: store.getCitizens(),
    practitioners: store.getPractitioners(),
    facilities: store.getFacilities()
  });

  assert.match(html, /name="code_challenge" value="chal-123"/);
  assert.match(html, /name="code_challenge_method" value="S256"/);
});

test('HTTP OAuth endpoints enforce PKCE on authorize and token exchange', async () => {
  const server = app.listen(0);
  const { port } = server.address();
  const baseUrl = `http://127.0.0.1:${port}`;

  try {
    const missingPkceRes = await fetch(`${baseUrl}/oauth/authorize?client_id=neuroscan-client&redirect_uri=http://localhost:3000/auth/callback&state=s1`);
    assert.equal(missingPkceRes.status, 400);

    const { verifier, challenge } = createPkcePair();
    const authPostRes = await fetch(`${baseUrl}/oauth/authorize`, {
      method: 'POST',
      redirect: 'manual',
      headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
      body: new URLSearchParams({
        cccd: '001200099999',
        client_id: 'neuroscan-client',
        redirect_uri: 'http://localhost:3000/auth/callback',
        state: 'state-ok',
        scope: 'openid profile',
        nonce: 'nonce-ok',
        code_challenge: challenge,
        code_challenge_method: 'S256'
      })
    });

    assert.equal(authPostRes.status, 302);
    const location = new URL(authPostRes.headers.get('location'));
    const code = location.searchParams.get('code');
    assert.ok(code);
    assert.equal(location.searchParams.get('state'), 'state-ok');

    const badTokenRes = await fetch(`${baseUrl}/oauth/token`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
      body: new URLSearchParams({
        grant_type: 'authorization_code',
        code,
        client_id: 'neuroscan-client',
        client_secret: 'neuroscan-secret-key-123456',
        redirect_uri: 'http://localhost:3000/auth/callback',
        code_verifier: `${verifier}wrong`
      })
    });
    assert.equal(badTokenRes.status, 400);

    const authPostRes2 = await fetch(`${baseUrl}/oauth/authorize`, {
      method: 'POST',
      redirect: 'manual',
      headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
      body: new URLSearchParams({
        cccd: '001200099999',
        client_id: 'neuroscan-client',
        redirect_uri: 'http://localhost:3000/auth/callback',
        state: 'state-ok-2',
        scope: 'openid profile',
        nonce: 'nonce-ok-2',
        code_challenge: challenge,
        code_challenge_method: 'S256'
      })
    });
    const code2 = new URL(authPostRes2.headers.get('location')).searchParams.get('code');

    const goodTokenRes = await fetch(`${baseUrl}/oauth/token`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
      body: new URLSearchParams({
        grant_type: 'authorization_code',
        code: code2,
        client_id: 'neuroscan-client',
        client_secret: 'neuroscan-secret-key-123456',
        redirect_uri: 'http://localhost:3000/auth/callback',
        code_verifier: verifier
      })
    });
    assert.equal(goodTokenRes.status, 200);
    const tokenJson = await goodTokenRes.json();
    assert.ok(tokenJson.id_token);
  } finally {
    await new Promise(resolve => server.close(resolve));
  }
});
