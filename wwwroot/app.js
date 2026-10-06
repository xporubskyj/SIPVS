const NAMESPACE = 'http://sipvs.example.sk/purchase-order';

const form = document.getElementById('order-form');
const items = document.getElementById('items');
const itemTemplate = document.getElementById('item-template');
const totalPrice = document.getElementById('total-price');
const result = document.getElementById('result');

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

function showResult(text, { link, isError = false } = {}) {
    result.textContent = text;
    result.classList.toggle('error', isError);
    if (link) {
        const anchor = document.createElement('a');
        anchor.href = link;
        anchor.target = '_blank';
        anchor.textContent = 'Otvoriť HTML';
        result.append(' ', anchor);
    }
}

function onClick(id, handler) {
    document.getElementById(id).addEventListener('click', async () => {
        try {
            await handler();
        } catch {
            showResult('Operácia zlyhala. Skontroluj, či je XML uložené.', { isError: true });
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