const NAMESPACE = 'http://sipvs.example.sk/purchase-order';

const form = document.getElementById('order-form');
const items = document.getElementById('items');
const itemTemplate = document.getElementById('item-template');
const totalPrice = document.getElementById('total-price');
const result = document.getElementById('result');
const XSD_REFERENCE = 'http://sipvs.example.sk/purchase-order/xsd/1.0';
const XSL_REFERENCE = 'http://sipvs.example.sk/purchase-order/xsl/1.0';
const PDF_FORMAT_ID = 'http://sipvs.example.sk/purchase-order/pdf/1.0';
const SHA256 = 'http://www.w3.org/2001/04/xmlenc#sha256';
const XML_FORMAT_ID = 'http://sipvs.example.sk/purchase-order/xml/1.0';
const XDC_NAMESPACE = 'http://data.gov.sk/def/container/xmldatacontainer+xml/1.1';

function addItem() {
    items.append(itemTemplate.content.cloneNode(true));
    updateTotal();
}

function removeItem(row) {
    if (items.rows.length > 1) {
        row.remove();
        updateTotal();
    }
}

function updateTotal() {
    let total = 0;
    for (const row of items.rows) {
        const quantity = Number(row.querySelector('.quantity').value);
        const unitPrice = Number(row.querySelector('.unit-price').value);
        total += quantity * unitPrice;
    }
    totalPrice.textContent = total.toFixed(2);
}

function buildXml() {
    const doc = document.implementation.createDocument(NAMESPACE, 'purchaseOrder');
    const order = doc.documentElement;
    order.setAttribute('currency', 'EUR');

    const append = (parent, name, value) => {
        const element = doc.createElementNS(NAMESPACE, name);
        if (value !== undefined) element.textContent = value;
        parent.append(element);
        return element;
    };

    append(order, 'customerName', document.getElementById('customer-name').value.trim());
    append(order, 'customerEmail', document.getElementById('customer-email').value.trim());
    append(order, 'orderDate', document.getElementById('order-date').value);

    const itemsElement = append(order, 'items');
    for (const row of items.rows) {
        const item = append(itemsElement, 'item');
        append(item, 'description', row.querySelector('.description').value.trim());
        append(item, 'quantity', row.querySelector('.quantity').value);
        append(item, 'unit', row.querySelector('.unit').value);
        append(item, 'unitPrice', Number(row.querySelector('.unit-price').value).toFixed(2));
    }

    append(order, 'totalPrice', totalPrice.textContent);
    return new XMLSerializer().serializeToString(doc);
}

async function post(url, body) {
    const response = await fetch(url, { method: 'POST', body });
    if (!response.ok) throw new Error();
    return response.json();
}


const stripDeclaration = text => text.replace(/^\uFEFF/, '').replace(/^\s*<\?xml[^>]*\?>\s*/, '');

async function getText(url) {
    const response = await fetch(url);
    if (!response.ok) throw new Error(`Nepodarilo sa načítať ${url}`);
    return stripDeclaration(await response.text());
}

function readFileAsBase64(file) {
    return new Promise((resolve, reject) => {
        const reader = new FileReader();
        reader.onload = () => resolve(reader.result.split(',')[1]);
        reader.onerror = () => reject(new Error('PDF sa nepodarilo prečítať.'));
        reader.readAsDataURL(file);
    });
}


async function signWithDBridge({ xml, xsd, xsl, pdfBase64 }) {
    const api = ditec.dSigXadesBpJs;
    const step = (method, ...args) => new Promise((resolve, reject) =>
        api[method](...args, {
            onSuccess: resolve,
            onError: e => reject(new Error(`D.Bridge (${method}): ${typeof e === 'string' ? e : e?.message ?? JSON.stringify(e)}`))
        }));

    await step('deploy', null);
    await step('initialize');
    await step('addXmlObject',
    'xmlObject',                  
    'Objednávka',                 
    XML_FORMAT_ID,                
    xml,                          
    NAMESPACE,                    
    '1.0',                        
    xsd,                          
    XSD_REFERENCE,                
    xsl,                          
    XSL_REFERENCE,                
    'HTML',                       
    'sk',                         
    '',                           
    false,                        
    XDC_NAMESPACE);               

    await step('addPdfObject', 'pdfObject', 'Príloha PDF', pdfBase64, '', PDF_FORMAT_ID, 2, false);
    await step('sign', 'signatureId', SHA256, '');
    return step('getSignatureWithASiCEnvelopeBase64');
}

onClick('sign', async () => {
    if (!form.reportValidity()) return;
    const file = document.getElementById('pdf-file').files[0];
    if (!file) throw new Error('Vyber PDF súbor na podpis.');

    showResult('Ukladám a overujem XML…');
    const xml = buildXml();
    await post('/api/save', xml);
    const { isValid, errors } = await post('/api/validate');
    if (!isValid) throw new Error(['XML nie je platné voči XSD:', ...errors].join('\n\n'));

    const [xsd, xsl, pdfBase64] = await Promise.all([
        getText('/api/forms/xsd'), getText('/api/forms/xsl'), readFileAsBase64(file)
    ]);

    showResult('Čakám na podpisovač (vyber certifikát)…');
    const asiceBase64 = await signWithDBridge({ xml, xsd, xsl, pdfBase64 });

    const { message } = await post('/api/save-asice', asiceBase64);
    showResult(message, { link: `/api/output/asice?t=${Date.now()}`, linkText: 'Stiahnuť .asice' });
});

function showResult(text, { link, linkText = 'Otvoriť HTML', isError = false } = {}) {
    result.textContent = text;
    result.classList.toggle('error', isError);
    if (link) {
        const anchor = document.createElement('a');
        anchor.href = link;
        anchor.target = '_blank';
        anchor.textContent = linkText;
        result.append(' ', anchor);
    }
}

function onClick(id, handler) {
    document.getElementById(id).addEventListener('click', async () => {
        try {
            await handler();
        } catch (e) {
            showResult(e.message || 'Operácia zlyhala. Skontroluj, či je XML uložené.', { isError: true });
        }
    });
}


onClick('save', async () => {
    if (!form.reportValidity()) return;
    const { message } = await post('/api/save', buildXml());
    showResult(message);
});

onClick('validate', async () => {
    const { isValid, errors } = await post('/api/validate');
    if (isValid) {
        showResult('XML je platné voči XSD.');
    } else {
        showResult(['XML nie je platné voči XSD:', ...errors].join('\n\n'), { isError: true });
    }
});

onClick('transform', async () => {
    const { message } = await post('/api/transform');
    showResult(message, { link: `/api/output/html?t=${Date.now()}` });
});

document.getElementById('add-item').addEventListener('click', addItem);
items.addEventListener('input', updateTotal);
items.addEventListener('click', event => {
    if (event.target.classList.contains('remove')) removeItem(event.target.closest('tr'));
});

addItem();

document.getElementById('order-date').valueAsDate = new Date();