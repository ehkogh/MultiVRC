window.multivrcReorder = {
    attach(container, dotnet) {
        if (!container || container._reorder) return;
        container._reorder = true;
        let start = null, item = null, ghost = null, dragging = false, over = null;

        container.addEventListener('pointerdown', e => {
            if (e.button !== 0) return;
            const found = e.target.closest('[data-reorder-id]');
            if (!found || !container.contains(found)) return;
            start = { x: e.clientX, y: e.clientY };
            item = found;
            dragging = false;
        });

        window.addEventListener('pointermove', e => {
            if (!item) return;
            if (!dragging) {
                if (Math.hypot(e.clientX - start.x, e.clientY - start.y) < 6) return;
                dragging = true;
                begin(e);
            }
            move(e);
        });

        window.addEventListener('pointerup', e => {
            if (!item) return;
            if (dragging) finish();
            item = null;
        });

        function begin(e) {
            const box = item.getBoundingClientRect();
            ghost = item.cloneNode(true);
            ghost.classList.add('reorder-ghost');
            Object.assign(ghost.style, {
                position: 'fixed', left: box.left + 'px', top: box.top + 'px', width: box.width + 'px', height: box.height + 'px',
                pointerEvents: 'none', zIndex: 1000, margin: 0,
            });
            ghost.offsetX = e.clientX - box.left;
            ghost.offsetY = e.clientY - box.top;
            document.body.appendChild(ghost);
            item.classList.add('reorder-source');
            document.body.classList.add('reordering');
        }

        function move(e) {
            ghost.style.left = (e.clientX - ghost.offsetX) + 'px';
            ghost.style.top = (e.clientY - ghost.offsetY) + 'px';
            const under = document.elementFromPoint(e.clientX, e.clientY)?.closest('[data-reorder-id]');
            const target = under && container.contains(under) && under !== item ? under : null;
            if (over !== target) {
                over?.classList.remove('reorder-over');
                over = target;
                over?.classList.add('reorder-over');
            }
        }

        function finish() {
            ghost.remove();
            ghost = null;
            item.classList.remove('reorder-source');
            document.body.classList.remove('reordering');
            const target = over;
            over?.classList.remove('reorder-over');
            over = null;
            const swallow = ev => { ev.stopPropagation(); ev.preventDefault(); };
            window.addEventListener('click', swallow, { capture: true, once: true });
            setTimeout(() => window.removeEventListener('click', swallow, { capture: true }), 60);
            if (target) dotnet.invokeMethodAsync('Reorder', item.dataset.reorderId, target.dataset.reorderId);
        }
    },
};
