(() => {
    const menus = [...document.querySelectorAll("[data-site-menu]")];

    for (const menu of menus) {
        menu.addEventListener("toggle", () => {
            if (!menu.open) {
                return;
            }

            for (const otherMenu of menus) {
                if (otherMenu !== menu) {
                    otherMenu.removeAttribute("open");
                }
            }
        });
    }

    document.addEventListener("click", event => {
        if (event.target.closest("[data-site-menu]")) {
            return;
        }

        for (const menu of menus) {
            menu.removeAttribute("open");
        }
    });

    document.addEventListener("keydown", event => {
        if (event.key !== "Escape") {
            return;
        }

        const openMenu = menus.find(menu => menu.open);
        if (!openMenu) {
            return;
        }

        openMenu.removeAttribute("open");
        openMenu.querySelector("summary")?.focus();
    });
})();
