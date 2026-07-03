// Tokeniza una tarjeta directamente contra Wompi desde el navegador: los datos de la tarjeta
// NUNCA llegan al servidor de ECOREX; solo el token (tok_...) vuelve a .NET via JSInterop.
window.ecorexTokenizeCard = async function (baseUrl, publicKey) {
    const val = (id) => (document.getElementById(id)?.value || '').trim();
    const body = {
        number: val('cc-number').replace(/\s+/g, ''),
        cvc: val('cc-cvc'),
        exp_month: val('cc-mm'),
        exp_year: val('cc-yy'),
        card_holder: val('cc-name')
    };
    try {
        const resp = await fetch(`${baseUrl}/tokens/cards`, {
            method: 'POST',
            headers: { 'Authorization': 'Bearer ' + publicKey, 'Content-Type': 'application/json' },
            body: JSON.stringify(body)
        });
        const data = await resp.json();
        if (resp.ok && data && data.data && data.data.id) {
            return { ok: true, token: data.data.id };
        }
        let msg = 'No se pudo validar la tarjeta.';
        if (data && data.error && data.error.messages) { msg = JSON.stringify(data.error.messages); }
        else if (data && data.error && data.error.reason) { msg = data.error.reason; }
        return { ok: false, error: msg };
    } catch (e) {
        return { ok: false, error: e?.message || 'Error de red al tokenizar.' };
    }
};
