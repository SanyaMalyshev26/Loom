const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { webcrypto } = require('node:crypto');
const scripts = path.join(__dirname, '../../wwwroot/js');
const sample = { variantId: 1, qty: 1, stock: 5, price: 1999.50, name: 'Товар', size: 'M', color: 'Чёрный' };
function storage(initial = {}) {
    const values = new Map(Object.entries(initial));
    return { getItem: key => values.get(key) ?? null, setItem: (key, value) => values.set(key, value), removeItem: key => values.delete(key) };
}
function browser(options = {}) {
    const local = options.local || storage({cart: JSON.stringify([sample])});
    const session = options.session || storage();
    const elements = new Map();
    const requests = [];
    function element() {
        return { disabled: false, textContent: '', innerHTML: '', dataset: {},
            classList: {add(){}, remove(){}}, appendChild(){},
            addEventListener(type, fn) { this[type] = fn; } };
    }
    const document = {
        getElementById(id) { if (!elements.has(id)) elements.set(id, element()); return elements.get(id); },
        createElement: element,
        querySelector(selector) {
            return selector.includes('data-currency')
                ? {dataset: {currencyCulture:'ru-RU', currencyCode:'RUB', checkoutUser: options.user || 'user-1'}}
                : {value:'test-antiforgery-token'};
        },
        querySelectorAll() { return []; }
    };
    document.getElementById('deliveryAddressId').value = '7';
    const context = vm.createContext({window:{location:{}}, document, localStorage:local, sessionStorage:session,
        crypto:webcrypto, Intl, console, setTimeout(){}, alert(){},
        fetch: async (url, request) => {
            requests.push({url, ...request});
            if (options.fetch) return options.fetch(url, request);
            return {status:200, ok:true, json:async()=>({orderId:42})};
        }
    });
    vm.runInContext(fs.readFileSync(path.join(scripts,'script.js'),'utf8'), context);
    vm.runInContext(fs.readFileSync(path.join(scripts,'cart.js'),'utf8'), context);
    return {context, local, session, requests, elements, click:()=>document.getElementById('checkoutBtn').click()};
}

test('corrupt, non-array and null storage entries recover without breaking scripts', () => {
    for (const cart of ['{broken', '{}', '[null]', 'null']) {
        const page = browser({local:storage({cart})});
        assert.equal(page.context.window.cart.length, 0);
    }
});

test('stored numeric fields are normalized and duplicate variants are capped at 99', () => {
    const page = browser({local:storage({cart:JSON.stringify([
        {...sample, variantId:'1', qty:'70'}, {...sample, qty:70},
        {...sample, variantId:-1}, {...sample, qty:1.5}, {...sample, price:-5}
    ])})});
    assert.equal(page.context.window.cart.length, 1);
    assert.equal(page.context.window.cart[0].qty, 99);
    assert.equal(typeof page.context.window.cart[0].variantId, 'number');
});

test('double click sends one request and clears the completed cart', async () => {
    let finish;
    const page = browser({fetch:()=>new Promise(resolve=>{finish=resolve;})});
    const pending = page.click();
    await page.click();
    assert.equal(page.requests.length, 1);
    assert.equal(page.elements.get('checkoutBtn').disabled, true);
    assert.match(page.requests[0].headers['Idempotency-Key'], /^[A-Za-z0-9._-]{16,128}$/);
    finish({status:200, ok:true, json:async()=>({orderId:42})});
    await pending;
    assert.equal(page.context.window.cart.length, 0);
    assert.equal(page.elements.get('checkoutBtn').disabled, false);
    assert.match(page.elements.get('checkoutMessage').textContent, /42/);
});

test('lost response and page reload reuse the same checkout key and payload', async () => {
    const first = browser({fetch:async()=>{throw new Error('Lost response');}});
    await first.click();
    assert.equal(first.context.window.cart.length, 1);
    const next = browser({local:first.local, session:first.session});
    await next.click();
    assert.equal(first.requests[0].headers['Idempotency-Key'], next.requests[0].headers['Idempotency-Key']);
    assert.equal(first.requests[0].body, next.requests[0].body);
});

test('server error without JSON remains retryable with the same key', async () => {
    const page = browser({fetch:async()=>({status:503, ok:false, json:async()=>{throw new Error('HTML response');}})});
    await page.click();
    await page.click();
    assert.equal(page.requests.length, 2);
    assert.equal(page.requests[0].headers['Idempotency-Key'], page.requests[1].headers['Idempotency-Key']);
    assert.equal(page.elements.get('checkoutBtn').disabled, false);
});

test('new payload and another account receive a different identity', async () => {
    const page = browser({fetch:async()=>{throw new Error('Offline');}});
    await page.click();
    page.context.window.cart[0].qty = 2;
    await page.click();
    assert.notEqual(page.requests[0].headers['Idempotency-Key'], page.requests[1].headers['Idempotency-Key']);
    const other = browser({local:page.local, session:page.session, user:'user-2'});
    await other.click();
    assert.notEqual(page.requests[0].headers['Idempotency-Key'], other.requests[0].headers['Idempotency-Key']);
});

test('unavailable retry storage prevents an unsafe checkout request', async () => {
    const session = {getItem(){throw new Error('Denied');}, setItem(){throw new Error('Denied');}};
    const page = browser({session});
    await page.click();
    assert.equal(page.requests.length, 0);
    assert.match(page.elements.get('checkoutMessage').textContent, /хранение/);
});

test('fractional prices are shown with kopecks', () => {
    const page = browser();
    assert.match(page.elements.get('grandTotal').textContent, /999,50/);
});
