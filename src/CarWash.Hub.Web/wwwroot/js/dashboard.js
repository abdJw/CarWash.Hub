// CarWash Hub dashboard helpers, called from Blazor through JS interop.
// Loaded as a plain script from this origin: the content-security policy allows no inline script.
(function () {
    "use strict";

    const themeKey = "carwashHub.theme";
    const soundKey = "carwashHub.sound";
    const playedKey = "carwashHub.sound.lastAlert";

    // localStorage can throw (private windows, blocked site data); a missing preference is not an
    // error, the dashboard then follows the operating system.
    function read(key) {
        try { return window.localStorage.getItem(key); } catch { return null; }
    }

    function write(key, value) {
        try { window.localStorage.setItem(key, value); } catch { /* not persisted; fine */ }
    }

    // Browsers only let a page make sound after the user has interacted with it. A Blazor click
    // travels to the server and back, which loses that gesture, so a plain listener here creates
    // and wakes the audio context on the first real tap or key press.
    let audio = null;

    function unlock() {
        try {
            const Context = window.AudioContext || window.webkitAudioContext;
            if (!Context) { return; }
            audio = audio || new Context();
            if (audio.state === "suspended") { audio.resume(); }
            if (audio.state === "running") {
                ["pointerdown", "keydown"].forEach(function (type) { document.removeEventListener(type, unlock, true); });
            }
        } catch { /* no sound on this device; fine */ }
    }

    ["pointerdown", "keydown"].forEach(function (type) { document.addEventListener(type, unlock, true); });

    // A short two-note chime, synthesised: no audio file to host or to allow in the CSP.
    function chime() {
        const start = audio.currentTime;
        [[880, 0], [1318.5, 0.14]].forEach(function (note) {
            const oscillator = audio.createOscillator();
            const gain = audio.createGain();
            const at = start + note[1];
            oscillator.type = "sine";
            oscillator.frequency.value = note[0];
            gain.gain.setValueAtTime(0.0001, at);
            gain.gain.exponentialRampToValueAtTime(0.2, at + 0.02);
            gain.gain.exponentialRampToValueAtTime(0.0001, at + 0.45);
            oscillator.connect(gain).connect(audio.destination);
            oscillator.start(at);
            oscillator.stop(at + 0.5);
        });
    }

    window.carwashHub = {
        print: function () { window.print(); },
        sound: {
            enabled: function () { return read(soundKey) !== "off"; },
            setEnabled: function (on) { write(soundKey, on ? "on" : "off"); },
            // Plays once per alert across this device's tabs; false when muted, already played
            // elsewhere, or the page has not been interacted with yet.
            play: function (alertId) {
                if (read(soundKey) === "off") { return false; }
                const last = Number(read(playedKey) || 0);
                if (alertId <= last) { return false; }
                write(playedKey, String(alertId));
                if (!audio || audio.state !== "running") { return false; }
                try { chime(); return true; } catch { return false; }
            }
        },
        theme: {
            get: function () {
                const value = read(themeKey);
                return value === "dark" || value === "light" ? value : null;
            },
            set: function (value) {
                if (value === "dark" || value === "light") {
                    write(themeKey, value);
                }
            }
        }
    };
})();
