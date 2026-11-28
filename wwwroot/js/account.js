(() => {
    const navigation = document.getElementById('accountTabs');
    const tabs = Array.from(navigation.querySelectorAll('[role="tab"]'));

    function showTabFromHash() {
        const tab = tabs.find(button => button.dataset.bsTarget === location.hash) ?? tabs[0];
        bootstrap.Tab.getOrCreateInstance(tab).show();
    }

    navigation.addEventListener('shown.bs.tab', event => {
        for (const tab of tabs) {
            tab.tabIndex = tab === event.target ? 0 : -1;
        }
        history.replaceState(history.state, '', event.target.dataset.bsTarget);
    });

    navigation.addEventListener('keydown', event => {
        const index = tabs.indexOf(event.target);
        if (index === -1) return;

        const next = {
            ArrowUp: (index - 1 + tabs.length) % tabs.length,
            ArrowLeft: (index - 1 + tabs.length) % tabs.length,
            ArrowDown: (index + 1) % tabs.length,
            ArrowRight: (index + 1) % tabs.length,
            Home: 0,
            End: tabs.length - 1
        }[event.key];

        if (next === undefined) return;
        event.preventDefault();
        tabs[next].focus();
        bootstrap.Tab.getOrCreateInstance(tabs[next]).show();
    });

    window.addEventListener('hashchange', showTabFromHash);
    showTabFromHash();
})();
