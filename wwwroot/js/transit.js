// CampusTransit shell helpers: theme persistence and the navigation drawer.
(function () {
    const KEY = "campustransit.theme";

    function apply(theme) {
        document.documentElement.setAttribute("data-theme", theme);
        document.documentElement.style.colorScheme = theme;
    }

    window.transitTheme = {
        // The bootstrap script in App.razor performs the first application.
        init: function () {
            if (typeof window.applyTransitTheme === "function") {
                window.applyTransitTheme();
            }

            const stored = localStorage.getItem(KEY);
            if (stored) {
                return stored;
            }

            return document.documentElement.getAttribute("data-theme") || "light";
        },
        get: function () {
            return document.documentElement.getAttribute("data-theme") || "light";
        },
        set: function (theme) {
            apply(theme);
            try {
                localStorage.setItem(KEY, theme);
            } catch (e) {
                // Storage can be unavailable in private browsing; the session still works.
            }
            return theme;
        }
    };

    // The drawer itself is pure CSS (a checkbox). These helpers stay for programmatic use.
    window.transitShell = {
        nav: function (open) {
            const toggle = document.getElementById("nav-drawer");
            if (toggle) {
                toggle.checked = !!open;
            }
        }
    };

    window.transitTheme.init();
})();
