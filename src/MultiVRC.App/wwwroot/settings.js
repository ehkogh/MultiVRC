window.multivrcSettings = {
    attach(nav) {
        if (!nav || nav._spy) return;
        nav._spy = true;
        const scroller = nav.closest('.content');
        const tabs = [...nav.querySelectorAll('[data-target]')];
        const sectionOf = tab => document.getElementById(tab.dataset.target);
        let pinned = null;

        const light = tab => tabs.forEach(t => t.classList.toggle('on', t === tab));

        const follow = () => {
            if (pinned) return light(pinned);
            const top = scroller.getBoundingClientRect().top + 90;
            const atBottom = scroller.scrollTop + scroller.clientHeight >= scroller.scrollHeight - 2;
            let current = tabs[0];
            for (const tab of tabs) {
                const section = sectionOf(tab);
                if (section && section.getBoundingClientRect().top <= top) current = tab;
            }
            light(atBottom && scroller.scrollTop > 0 ? tabs[tabs.length - 1] : current);
        };

        tabs.forEach(tab => tab.addEventListener('click', () => {
            pinned = tab;
            light(tab);
            sectionOf(tab)?.scrollIntoView({ behavior: 'smooth', block: 'start' });
        }));

        const unpin = () => { pinned = null; };
        scroller.addEventListener('wheel', unpin, { passive: true });
        scroller.addEventListener('pointerdown', unpin);
        scroller.addEventListener('keydown', unpin);
        scroller.addEventListener('scroll', follow, { passive: true });

        new MutationObserver((_, observer) => {
            if (document.body.contains(nav)) return;
            observer.disconnect();
            scroller.removeEventListener('wheel', unpin);
            scroller.removeEventListener('pointerdown', unpin);
            scroller.removeEventListener('keydown', unpin);
            scroller.removeEventListener('scroll', follow);
        }).observe(scroller, { childList: true, subtree: true });

        follow();
    },
};
