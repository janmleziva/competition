const countdown = document.querySelector("[data-countdown-target]");

if (countdown) {
    const target = new Date(countdown.dataset.countdownTarget).getTime();
    const values = Object.fromEntries(
        [...countdown.querySelectorAll("[data-unit]")].map(element => [element.dataset.unit, element])
    );

    const tick = () => {
        const remaining = Math.max(0, target - Date.now());
        const totalSeconds = Math.floor(remaining / 1000);
        const units = {
            days: Math.floor(totalSeconds / 86400),
            hours: Math.floor((totalSeconds % 86400) / 3600),
            minutes: Math.floor((totalSeconds % 3600) / 60),
            seconds: totalSeconds % 60
        };

        Object.entries(units).forEach(([name, value]) => {
            values[name].textContent = String(value).padStart(2, "0");
        });

        if (remaining === 0) {
            document.querySelector("h1").textContent = "Soutěž právě probíhá";
            document.querySelector(".lead").textContent = "Soutěž už začala.";
        }
    };

    tick();
    window.setInterval(tick, 1000);
}
