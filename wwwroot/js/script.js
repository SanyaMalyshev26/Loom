// One validated storage format is shared by the catalog and the cart page.
window.LoomCart = (() => {
    function load() {
        try {
            const stored = JSON.parse(localStorage.getItem('cart') || '[]');
            if (!Array.isArray(stored)) return [];
            const lines = new Map();
            for (const item of stored) {
                if (!item || typeof item !== 'object') continue;
                const variantId = Number(item.variantId);
                const qty = Number(item.qty);
                const price = Number(item.price);
                const stock = Number(item.stock);
                if (!Number.isSafeInteger(variantId) || variantId < 1 ||
                    !Number.isInteger(qty) || qty < 1 ||
                    !Number.isFinite(price) || price < 0 ||
                    !Number.isInteger(stock) || stock < 0) continue;
                const previous = lines.get(variantId);
                lines.set(variantId, {
                    variantId,
                    qty: Math.min(99, qty + (previous?.qty || 0)),
                    price,
                    stock,
                    name: String(item.name ?? '').slice(0, 120),
                    size: String(item.size ?? '').slice(0, 32),
                    color: String(item.color ?? '').slice(0, 64)
                });
            }
            return Array.from(lines.values()).slice(0, 100);
        } catch {
            return [];
        }
    }
    function save(cart) {
        try {
            localStorage.setItem('cart', JSON.stringify(cart));
            return true;
        } catch {
            // Keep the current page usable when browser storage is unavailable.
            return false;
        }
    }
    return { load, save };
})();

window.cart = window.LoomCart.load();
window.LoomCart.save(window.cart);

function updateCartBadge() {
    const badge = document.getElementById('cartBadge');
    if (badge) badge.textContent = window.cart.reduce((sum, item) => sum + item.qty, 0);
}

document.querySelectorAll('.variant-select').forEach(select => {
    select.addEventListener('change', () => {
        const button = select.closest('.product-card')?.querySelector('.add-to-cart');
        if (button) button.disabled = !select.value;
    });
});

document.querySelectorAll('.add-to-cart').forEach(btn => {
    btn.addEventListener('click', () => {
        const select = btn.closest('.product-card').querySelector('.variant-select');
        const option = select.selectedOptions[0];
        if (!option) return;
        const variantId = Number(select.value);
        const stock = Number(option.dataset.stock);
        const price = Number(btn.dataset.price);
        if (!Number.isSafeInteger(variantId) || variantId < 1 || stock < 1 ||
            !Number.isFinite(price) || price < 0) return;
        const existing = window.cart.find(item => item.variantId === variantId);
        if (existing) {
            if (existing.qty >= Math.min(stock, 99)) {
                alert('Достигнуто доступное количество: не более 99 единиц одного варианта.');
                return;
            }
            existing.qty++;
            existing.stock = stock;
            existing.price = price;
        } else {
            if (window.cart.length >= 100) {
                alert('В одном заказе может быть не более 100 вариантов.');
                return;
            }
            window.cart.push({ variantId, name: btn.dataset.name, size: option.dataset.size,
                color: option.dataset.color, price, qty: 1, stock });
        }
        window.LoomCart.save(window.cart);
        updateCartBadge();
        btn.textContent = '✓ Добавлено';
        setTimeout(() => btn.textContent = 'В корзину', 800);
    });
});

updateCartBadge();
