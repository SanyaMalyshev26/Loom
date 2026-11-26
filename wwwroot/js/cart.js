const tbody = document.getElementById('cartTableBody');
const emptyBlock = document.getElementById('emptyCart');
const contentBlock = document.getElementById('cartContent');
const currencyContainer = document.querySelector('[data-currency-code]');
const currencyFormatter = new Intl.NumberFormat(currencyContainer.dataset.currencyCulture, {
    style: 'currency',
    currency: currencyContainer.dataset.currencyCode,
    minimumFractionDigits: 2,
    maximumFractionDigits: 2
});

// Сохранение и обновление счётчика в шапке
function saveCart() {
    const saved = window.LoomCart.save(window.cart);
    updateCartBadge();
    return saved; // функция из script.js
}

// Форматирование цены
function formatPrice(value) {
    return currencyFormatter.format(value);
}

// Рендер всей корзины
function renderCart() {
    tbody.innerHTML = '';
    if (window.cart.length === 0) {
        updateTotals();
        emptyBlock.classList.remove('d-none');
        contentBlock.classList.add('d-none');
        return;
    }
    emptyBlock.classList.add('d-none');
    contentBlock.classList.remove('d-none');

    window.cart.forEach((item, index) => {
        const row = document.createElement('tr');

        row.innerHTML = `
            <td>
                <div class="d-flex align-items-center gap-3">
                    <img src="https://placehold.co/60x60" alt="" width="60" height="60" class="rounded">
                    <span class="fw-semibold">${escapeHtml(item.name)}<br>
                        <small class="text-muted">${escapeHtml(item.size)} · ${escapeHtml(item.color)}</small>
                    </span>
                </div>
            </td>
            <td class="text-center">
                <div class="input-group input-group-sm justify-content-center" style="max-width:120px; margin:auto;">
                    <button class="btn btn-outline-secondary btn-minus" data-index="${index}" ${checkoutInProgress ? 'disabled' : ''}>−</button>
                    <input type="text" class="form-control text-center" value="${item.qty}" readonly>
                    <button class="btn btn-outline-secondary btn-plus" data-index="${index}"
                            ${item.qty >= Math.min(item.stock, 99) || checkoutInProgress ? 'disabled' : ''}>+</button>
                </div>
            </td>
            <td class="text-end">${formatPrice(item.price)}</td>
            <td class="text-end fw-semibold">${formatPrice(item.price * item.qty)}</td>
            <td class="text-end">
                <button class="btn btn-sm btn-outline-danger btn-remove" data-index="${index}" ${checkoutInProgress ? 'disabled' : ''}>✕</button>
            </td>
        `;

        tbody.appendChild(row);
    });

    attachRowEvents();
    updateTotals();
}

function escapeHtml(value) {
    return String(value ?? '').replace(/[&<>"']/g, character => ({
        '&': '&amp;',
        '<': '&lt;',
        '>': '&gt;',
        '"': '&quot;',
        "'": '&#39;'
    })[character]);
}

// Обработчики кнопок +/− и удаления
function attachRowEvents() {
    document.querySelectorAll('.btn-plus').forEach(btn => {
        btn.addEventListener('click', () => {
            if (checkoutInProgress) return;
            const i = parseInt(btn.dataset.index);
            if (checkoutInProgress || window.cart[i].qty >= Math.min(window.cart[i].stock, 99)) return;
            window.cart[i].qty++;
            saveCart();
            renderCart();
        });
    });

    document.querySelectorAll('.btn-minus').forEach(btn => {
        btn.addEventListener('click', () => {
            if (checkoutInProgress) return;
            const i = parseInt(btn.dataset.index);
            if (window.cart[i].qty > 1) {
                window.cart[i].qty--;
            } else {
                window.cart.splice(i, 1);
            }
            saveCart();
            renderCart();
        });
    });

    document.querySelectorAll('.btn-remove').forEach(btn => {
        btn.addEventListener('click', () => {
            if (checkoutInProgress) return;
            const i = parseInt(btn.dataset.index);
            window.cart.splice(i, 1);
            saveCart();
            renderCart();
        });
    });
}

// Подсчёт итогов
function updateTotals() {
    const totalItems = window.cart.reduce((sum, i) => sum + i.qty, 0);
    const subtotal = window.cart.reduce((sum, i) => sum + i.price * i.qty, 0);

    document.getElementById('totalItems').textContent = totalItems;
    document.getElementById('subtotal').textContent = formatPrice(subtotal);
    document.getElementById('grandTotal').textContent = formatPrice(subtotal);
}

// Keep one checkout identity across network retries and page reloads in this tab.
const checkoutButton = document.getElementById('checkoutBtn');
const checkoutMessage = document.getElementById('checkoutMessage');
const addressSelect = document.getElementById('deliveryAddressId');
const checkoutInitiallyDisabled = checkoutButton.disabled;
const pendingStorageKey = `loom.checkout.${currencyContainer.dataset.checkoutUser || 'anonymous'}`;
let checkoutInProgress = false;

function showCheckoutMessage(message, success = false) {
    checkoutMessage.textContent = message;
    checkoutMessage.className = `alert ${success ? 'alert-success' : 'alert-warning'}`;
}

function checkoutIdentity(payload) {
    const fingerprint = JSON.stringify(payload);
    // Do not send if durable retry storage is unavailable: a lost response must
    // not turn the next click or reload into a new order.
    let pending;
    try { pending = JSON.parse(sessionStorage.getItem(pendingStorageKey) || 'null'); }
    catch { pending = null; }
    if (pending?.fingerprint === fingerprint &&
        typeof pending.key === 'string' && /^[A-Za-z0-9._-]{16,128}$/.test(pending.key)) {
        return pending.key;
    }
    const bytes = crypto.getRandomValues(new Uint8Array(16));
    const key = 'checkout-' + Array.from(bytes, byte => byte.toString(16).padStart(2, '0')).join('');
    sessionStorage.setItem(pendingStorageKey, JSON.stringify({ fingerprint, key }));
    return key;
}

checkoutButton.addEventListener('click', async () => {
    if (checkoutInProgress || checkoutInitiallyDisabled || window.cart.length === 0) return;
    const payload = {
        addressId: addressSelect ? Number(addressSelect.value) : 0,
        lines: window.cart.map(item => ({ variantId: item.variantId, qty: item.qty }))
            .sort((left, right) => left.variantId - right.variantId)
    };
    let idempotencyKey;
    try {
        idempotencyKey = checkoutIdentity(payload);
    } catch {
        showCheckoutMessage('Для безопасного оформления разрешите хранение данных сайта в браузере и повторите попытку.');
        return;
    }
    checkoutInProgress = true;
    checkoutButton.disabled = true;
    checkoutButton.textContent = 'Оформляем заказ…';
    if (addressSelect) addressSelect.disabled = true;
    renderCart();
    showCheckoutMessage('Отправляем заказ. Пожалуйста, дождитесь ответа.');
    try {
        const token = document.querySelector('input[name="__RequestVerificationToken"]').value;
        const response = await fetch('/Orders/Create', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': token,
                'Idempotency-Key': idempotencyKey
            },
            body: JSON.stringify(payload)
        });
        if (response.status === 401) {
            window.location.href = '/Account/Login?returnUrl=/Cart';
            return;
        }
        const result = await response.json().catch(() => null);
        if (!response.ok) {
            showCheckoutMessage(result?.message || (response.status === 400
                ? 'Проверьте адрес доставки. Если сессия истекла, обновите страницу.'
                : 'Не удалось подтвердить оформление. Повторите попытку с тем же составом и адресом — повторный запрос защищён от дубля.'));
            return;
        }
        if (!Number.isSafeInteger(result?.orderId) || result.orderId < 1)
            throw new Error('Invalid checkout response');
        // Remove cart contents before clearing the retry identity. If storage
        // cleanup fails, retrying still returns the already-created order.
        window.cart = [];
        const cartSaved = saveCart();
        if (cartSaved) {
            try { sessionStorage.removeItem(pendingStorageKey); } catch { /* harmless stale identity */ }
        }
        showCheckoutMessage(`Заказ #${result.orderId} оформлен. Он доступен в личном кабинете.`, true);
    } catch {
        showCheckoutMessage('Не удалось получить подтверждение заказа. Повторите попытку с тем же составом и адресом — сохранённый ключ защитит от дубля.');
    } finally {
        checkoutInProgress = false;
        checkoutButton.disabled = checkoutInitiallyDisabled;
        checkoutButton.textContent = 'Оформить заказ';
        if (addressSelect) addressSelect.disabled = false;
        renderCart();
    }
});

renderCart();
