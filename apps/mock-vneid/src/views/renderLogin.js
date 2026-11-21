const fs = require('fs');
const path = require('path');

const loginHtmlTemplate = fs.readFileSync(path.resolve(__dirname, 'login.html'), 'utf8');

function escapeHtmlAttr(value) {
  return String(value || '')
    .replace(/&/g, '&amp;')
    .replace(/"/g, '&quot;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;');
}

function renderLoginPage({ clientId, redirectUri, state, scope, nonce, codeChallenge, codeChallengeMethod, citizens, practitioners, facilities, error }) {
  const facList = facilities || [];
  const practByCccd = new Map(practitioners.map(p => [p.cccd, p]));

  // Sort practitioners first so clinicians appear immediately in dropdown, followed by non-practitioner test citizens
  const sortedCitizens = [...citizens].sort((a, b) => {
    const pa = practByCccd.get(a.cccd);
    const pb = practByCccd.get(b.cccd);
    if (pa && !pb) return -1;
    if (!pa && pb) return 1;
    if (pa && pb) {
      if (pa.ma_cskcb_cong_tac !== pb.ma_cskcb_cong_tac) {
        return pa.ma_cskcb_cong_tac.localeCompare(pb.ma_cskcb_cong_tac);
      }
      return (pa.ma_chuc_danh || '').localeCompare(pb.ma_chuc_danh || '');
    }
    return 0;
  });

  const dropdownItems = sortedCitizens.map(c => {
    const pract = practByCccd.get(c.cccd);
    const facility = pract ? facList.find(f => f.ma_bv === pract.ma_cskcb_cong_tac) : null;
    const facilityName = facility ? `${facility.ten_bv} (${facility.ma_bv})` : (pract ? `Mã BV: ${pract.ma_cskcb_cong_tac}` : null);

    const roleBadge = pract
      ? `${pract.ma_chuc_danh || 'BS'} • ${pract.ten_chuc_danh || pract.pham_vi_hoat_dong}${pract.trang_thai !== 'Active' ? ` • ${pract.trang_thai}` : ''}`
      : `Công dân • Không có CCHN`;

    const subText = pract
      ? `CCCD: <span class="font-mono">${c.cccd}</span> • <strong>${facilityName}</strong> • CCHN: ${pract.ma_cchn} • ${pract.pham_vi_hoat_dong}`
      : `CCCD: <span class="font-mono">${c.cccd}</span> • BHYT: ${c.so_the_bhyt || 'N/A'} (Không có CCHN)`;

    const searchText = escapeHtmlAttr(
      `${c.cccd} ${c.ho_va_ten} ${pract ? `${pract.ma_cchn} ${pract.ma_chuc_danh || ''} ${pract.ten_chuc_danh || ''} ${pract.pham_vi_hoat_dong} ${pract.ma_cskcb_cong_tac} ${facilityName || ''} ${pract.trang_thai}` : 'cong dan'}`
        .toLowerCase()
    );

    return `<div class="combobox-item" data-cccd="${escapeHtmlAttr(c.cccd)}" data-search="${searchText}"><div class="combobox-item-name">${escapeHtmlAttr(c.ho_va_ten)} <span class="combobox-item-badge">${escapeHtmlAttr(roleBadge)}</span></div><div class="combobox-item-sub">${subText}</div></div>`;
  }).join('');

  const errorSection = error
    ? `<div class="error-box">${escapeHtmlAttr(error)}</div>`
    : '';

  return loginHtmlTemplate
    .replace('{{CLIENT_ID}}', escapeHtmlAttr(clientId))
    .replace('{{REDIRECT_URI}}', escapeHtmlAttr(redirectUri))
    .replace('{{STATE}}', escapeHtmlAttr(state))
    .replace('{{SCOPE}}', escapeHtmlAttr(scope))
    .replace('{{NONCE}}', escapeHtmlAttr(nonce))
    .replace('{{CODE_CHALLENGE}}', escapeHtmlAttr(codeChallenge))
    .replace('{{CODE_CHALLENGE_METHOD}}', escapeHtmlAttr(codeChallengeMethod))
    .replace('{{ERROR_SECTION}}', errorSection)
    .replace('{{DROPDOWN_ITEMS}}', dropdownItems);
}

module.exports = {
  renderLoginPage
};
