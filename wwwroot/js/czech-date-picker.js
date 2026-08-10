document.addEventListener("DOMContentLoaded", () => {
    const monthNames = [
        "leden", "únor", "březen", "duben", "květen", "červen",
        "červenec", "srpen", "září", "říjen", "listopad", "prosinec"
    ];
    const weekdayNames = ["Po", "Út", "St", "Čt", "Pá", "So", "Ne"];

    document.querySelectorAll("input[data-czech-date-picker='true'], input[data-czech-datetime-picker='true']").forEach(input => {
        const isDateTime = input.dataset.czechDatetimePicker === "true";
        const wrapper = document.createElement("div");
        wrapper.className = "date-picker";
        input.parentNode.insertBefore(wrapper, input);
        wrapper.appendChild(input);

        const toggle = document.createElement("button");
        toggle.type = "button";
        toggle.className = "date-picker-toggle";
        toggle.setAttribute("aria-label", isDateTime ? "Vybrat datum a čas" : "Vybrat datum");
        toggle.setAttribute("aria-expanded", "false");
        toggle.textContent = "◦";
        wrapper.appendChild(toggle);

        const popup = document.createElement("div");
        popup.className = "date-picker-popup";
        popup.hidden = true;
        popup.setAttribute("role", "dialog");
        popup.setAttribute("aria-label", isDateTime ? "Kalendář pro výběr data a času" : "Kalendář pro výběr data");
        wrapper.appendChild(popup);

        let viewedDate = parseInputValue(input.value, isDateTime) ?? new Date();
        viewedDate = new Date(viewedDate.getFullYear(), viewedDate.getMonth(), 1);

        toggle.addEventListener("click", () => {
            const opening = popup.hidden;
            closeAllPickers(popup);
            popup.hidden = !opening;
            toggle.setAttribute("aria-expanded", String(opening));
            if (opening) {
                const selected = parseInputValue(input.value, isDateTime);
                if (selected) {
                    viewedDate = new Date(selected.getFullYear(), selected.getMonth(), 1);
                }
                render();
            }
        });

        document.addEventListener("click", event => {
            if (!wrapper.contains(event.target)) {
                popup.hidden = true;
                toggle.setAttribute("aria-expanded", "false");
            }
        });

        wrapper.addEventListener("keydown", event => {
            if (event.key === "Escape") {
                popup.hidden = true;
                toggle.setAttribute("aria-expanded", "false");
                toggle.focus();
            }
        });

        function render() {
            popup.replaceChildren();

            const header = document.createElement("div");
            header.className = "date-picker-header";
            header.appendChild(navigationButton("‹", "Předchozí měsíc", -1));

            const selectors = document.createElement("div");
            selectors.className = "date-picker-selectors";

            const monthSelect = document.createElement("select");
            monthSelect.setAttribute("aria-label", "Měsíc");
            monthNames.forEach((name, month) => {
                monthSelect.add(new Option(name, String(month), false, month === viewedDate.getMonth()));
            });
            monthSelect.addEventListener("change", () => {
                viewedDate = new Date(viewedDate.getFullYear(), Number(monthSelect.value), 1);
                render();
            });

            const yearSelect = document.createElement("select");
            yearSelect.setAttribute("aria-label", "Rok");
            const currentYear = new Date().getFullYear();
            const oldestYear = Math.min(currentYear - 120, viewedDate.getFullYear());
            for (let year = currentYear; year >= oldestYear; year--) {
                yearSelect.add(new Option(String(year), String(year), false, year === viewedDate.getFullYear()));
            }
            yearSelect.addEventListener("change", () => {
                viewedDate = new Date(Number(yearSelect.value), viewedDate.getMonth(), 1);
                render();
            });

            selectors.append(monthSelect, yearSelect);
            header.appendChild(selectors);
            header.appendChild(navigationButton("›", "Následující měsíc", 1));
            popup.appendChild(header);

            const grid = document.createElement("div");
            grid.className = "date-picker-grid";
            weekdayNames.forEach(name => {
                const weekday = document.createElement("span");
                weekday.className = "date-picker-weekday";
                weekday.textContent = name;
                grid.appendChild(weekday);
            });

            const firstWeekday = (new Date(viewedDate.getFullYear(), viewedDate.getMonth(), 1).getDay() + 6) % 7;
            const daysInMonth = new Date(viewedDate.getFullYear(), viewedDate.getMonth() + 1, 0).getDate();
            for (let index = 0; index < firstWeekday; index++) {
                grid.appendChild(document.createElement("span"));
            }

            const selected = parseInputValue(input.value, isDateTime);
            for (let day = 1; day <= daysInMonth; day++) {
                const button = document.createElement("button");
                button.type = "button";
                button.className = "date-picker-day";
                button.textContent = String(day);
                button.setAttribute("aria-label", `${day}. ${monthNames[viewedDate.getMonth()]} ${viewedDate.getFullYear()}`);
                if (selected &&
                    selected.getFullYear() === viewedDate.getFullYear() &&
                    selected.getMonth() === viewedDate.getMonth() &&
                    selected.getDate() === day) {
                    button.classList.add("is-selected");
                }
                button.addEventListener("click", () => {
                    const nextValue = new Date(viewedDate.getFullYear(), viewedDate.getMonth(), day);
                    const existing = parseInputValue(input.value, isDateTime);
                    if (isDateTime && existing) {
                        nextValue.setHours(existing.getHours(), existing.getMinutes(), 0, 0);
                    }
                    input.value = isDateTime ? formatDateTime(nextValue) : formatDate(nextValue);
                    input.dispatchEvent(new Event("change", { bubbles: true }));

                    if (isDateTime) {
                        render();
                    } else {
                        popup.hidden = true;
                        toggle.setAttribute("aria-expanded", "false");
                        input.focus();
                    }
                });
                grid.appendChild(button);
            }
            popup.appendChild(grid);

            if (isDateTime) {
                const timeRow = document.createElement("div");
                timeRow.className = "date-time-picker-time";

                const current = parseInputValue(input.value, true) ?? new Date();

                const hourField = document.createElement("input");
                hourField.type = "number";
                hourField.min = "0";
                hourField.max = "23";
                hourField.step = "1";
                hourField.inputMode = "numeric";
                hourField.value = String(current.getHours()).padStart(2, "0");
                hourField.setAttribute("aria-label", "Hodina");

                const separator = document.createElement("span");
                separator.textContent = ":";

                const minuteField = document.createElement("input");
                minuteField.type = "number";
                minuteField.min = "0";
                minuteField.max = "59";
                minuteField.step = "1";
                minuteField.inputMode = "numeric";
                minuteField.value = String(current.getMinutes()).padStart(2, "0");
                minuteField.setAttribute("aria-label", "Minuta");

                const applyButton = document.createElement("button");
                applyButton.type = "button";
                applyButton.className = "button button-small";
                applyButton.textContent = "Použít";
                applyButton.addEventListener("click", () => {
                    const baseDate = parseInputValue(input.value, true) ?? new Date(viewedDate.getFullYear(), viewedDate.getMonth(), 1);
                    baseDate.setHours(
                        clampNumber(hourField.value, 0, 23),
                        clampNumber(minuteField.value, 0, 59),
                        0,
                        0);
                    input.value = formatDateTime(baseDate);
                    input.dispatchEvent(new Event("change", { bubbles: true }));
                    popup.hidden = true;
                    toggle.setAttribute("aria-expanded", "false");
                    input.focus();
                });

                timeRow.append(hourField, separator, minuteField, applyButton);
                popup.appendChild(timeRow);
            }
        }

        function navigationButton(text, label, monthDelta) {
            const button = document.createElement("button");
            button.type = "button";
            button.textContent = text;
            button.setAttribute("aria-label", label);
            button.addEventListener("click", () => {
                viewedDate = new Date(viewedDate.getFullYear(), viewedDate.getMonth() + monthDelta, 1);
                render();
            });
            return button;
        }
    });

    function closeAllPickers(except) {
        document.querySelectorAll(".date-picker-popup").forEach(popup => {
            if (popup !== except) {
                popup.hidden = true;
            }
        });
    }

    function parseInputValue(value, withTime) {
        const trimmed = value.trim();
        if (trimmed.length === 0) {
            return null;
        }

        if (withTime) {
            const dateTimeMatch = /^(\d{1,2})[/.](\d{1,2})[/.](\d{4})\s+(\d{1,2}):(\d{2})$/.exec(trimmed);
            if (!dateTimeMatch) {
                return null;
            }

            const dateTime = new Date(
                Number(dateTimeMatch[3]),
                Number(dateTimeMatch[2]) - 1,
                Number(dateTimeMatch[1]),
                Number(dateTimeMatch[4]),
                Number(dateTimeMatch[5]));
            return isSameDateTime(dateTime, dateTimeMatch) ? dateTime : null;
        }

        const dateMatch = /^(\d{1,2})[/.](\d{1,2})[/.](\d{4})$/.exec(trimmed);
        if (!dateMatch) {
            return null;
        }

        const date = new Date(Number(dateMatch[3]), Number(dateMatch[2]) - 1, Number(dateMatch[1]));
        return date.getFullYear() === Number(dateMatch[3]) &&
            date.getMonth() === Number(dateMatch[2]) - 1 &&
            date.getDate() === Number(dateMatch[1]) ? date : null;
    }

    function formatDate(date) {
        return `${String(date.getDate()).padStart(2, "0")}/${String(date.getMonth() + 1).padStart(2, "0")}/${date.getFullYear()}`;
    }

    function formatDateTime(date) {
        return `${formatDate(date)} ${String(date.getHours()).padStart(2, "0")}:${String(date.getMinutes()).padStart(2, "0")}`;
    }

    function clampNumber(value, min, max) {
        const parsed = Number(value);
        if (Number.isNaN(parsed)) {
            return min;
        }

        return Math.min(max, Math.max(min, parsed));
    }

    function isSameDateTime(date, match) {
        return date.getFullYear() === Number(match[3]) &&
            date.getMonth() === Number(match[2]) - 1 &&
            date.getDate() === Number(match[1]) &&
            date.getHours() === Number(match[4]) &&
            date.getMinutes() === Number(match[5]);
    }
});
