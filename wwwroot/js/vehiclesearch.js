// wwwroot/js/vehiclesearch.js
//
// Live suggestions under the home page's search box, fed by
// Endpoints/VehicleEndpoints.cs's GET /api/vehicles/search-suggestions. Picking a suggestion
// navigates straight to that vehicle -- the plain form submit below it still works exactly as
// before for anyone who just types and hits Enter or Search.
(function () {
    const input = document.getElementById('vehicle-search-input');
    const list = document.getElementById('vehicle-search-suggestions');
    if (!input || !list) return;

    const MIN_LENGTH = 2;
    const DEBOUNCE_MS = 200;

    let debounceHandle = null;
    let activeIndex = -1;
    let currentSuggestions = [];
    let requestToken = 0;

    function hide() {
        list.classList.add('d-none');
        list.innerHTML = '';
        activeIndex = -1;
        currentSuggestions = [];
    }

    function labelFor(vehicle) {
        const identifier = vehicle.vin || vehicle.reg || '(no VIN/Reg)';
        return vehicle.makeModel ? `${identifier} — ${vehicle.makeModel}` : identifier;
    }

    function render(vehicles) {
        currentSuggestions = vehicles;
        activeIndex = -1;

        if (vehicles.length === 0) {
            hide();
            return;
        }

        list.innerHTML = '';
        vehicles.forEach((vehicle) => {
            const item = document.createElement('button');
            item.type = 'button';
            item.className = 'list-group-item list-group-item-action';
            item.textContent = labelFor(vehicle);
            item.addEventListener('mousedown', (e) => {
                // mousedown, not click -- fires before the input's blur handler hides the list.
                e.preventDefault();
                navigateTo(vehicle);
            });
            list.appendChild(item);
        });
        list.classList.remove('d-none');
    }

    function navigateTo(vehicle) {
        window.location.href = `/Vehicles/Details/${vehicle.id}`;
    }

    function highlight(index) {
        const items = list.querySelectorAll('.list-group-item');
        items.forEach((el, i) => el.classList.toggle('active', i === index));
        activeIndex = index;
    }

    async function fetchSuggestions(term) {
        const token = ++requestToken;
        try {
            const response = await fetch(`/api/vehicles/search-suggestions?term=${encodeURIComponent(term)}`);
            if (!response.ok) return;
            const vehicles = await response.json();
            if (token !== requestToken) return; // a newer keystroke has already superseded this
            render(vehicles);
        } catch {
            // A dropped network request just means no suggestions this keystroke -- the plain
            // Search button is still right there and unaffected.
        }
    }

    input.addEventListener('input', () => {
        const term = input.value.trim();
        window.clearTimeout(debounceHandle);

        if (term.length < MIN_LENGTH) {
            hide();
            return;
        }

        debounceHandle = window.setTimeout(() => fetchSuggestions(term), DEBOUNCE_MS);
    });

    input.addEventListener('keydown', (e) => {
        if (list.classList.contains('d-none') || currentSuggestions.length === 0) return;

        if (e.key === 'ArrowDown') {
            e.preventDefault();
            highlight((activeIndex + 1) % currentSuggestions.length);
        } else if (e.key === 'ArrowUp') {
            e.preventDefault();
            highlight((activeIndex - 1 + currentSuggestions.length) % currentSuggestions.length);
        } else if (e.key === 'Enter' && activeIndex >= 0) {
            e.preventDefault();
            navigateTo(currentSuggestions[activeIndex]);
        } else if (e.key === 'Escape') {
            hide();
        }
    });

    input.addEventListener('blur', () => window.setTimeout(hide, 100));
})();
