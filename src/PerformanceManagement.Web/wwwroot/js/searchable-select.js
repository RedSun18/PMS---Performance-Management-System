// Progressive-enhancement searchable combobox for any <select data-searchable>.
//
// The original <select> is never removed or replaced — it keeps its id, name, value and
// options exactly as server-rendered (or as later mutated by page-specific script, e.g. the KPI/
// Competency perspective filters in PmForm). It's only visually hidden. A text input + listbox
// overlay is inserted next to it to provide typing-to-filter, keyboard navigation and mouse
// selection; picking an option sets select.value and dispatches a real, bubbling 'change' event
// on the select, so every existing onchange="..." attribute, addEventListener('change', ...), and
// getElementById(selectId).value read elsewhere in the app keeps working completely unchanged.
//
// Usage: add `data-searchable` to any <select> with a potentially long option list. No other
// per-page wiring is needed — enhancement runs automatically on DOMContentLoaded and again for
// any select added later via window.SearchableSelect.enhanceAll(container).
(function () {
    'use strict';

    var idSeq = 0;

    function optionsOf(select) {
        var list = [];
        for (var i = 0; i < select.options.length; i++) {
            var opt = select.options[i];
            list.push({ value: opt.value, text: opt.textContent, disabled: opt.disabled });
        }
        return list;
    }

    function selectedOption(select) {
        var opt = select.options[select.selectedIndex];
        return opt ? { value: opt.value, text: opt.textContent } : null;
    }

    function enhance(select) {
        if (select.dataset.sselReady || select.tagName !== 'SELECT') return;
        select.dataset.sselReady = '1';

        var noResultsText = document.body.dataset.noResultsText || 'No results found.';
        idSeq++;
        var listboxId = 'ssel-listbox-' + idSeq;
        var inputId = select.id ? select.id + '-search' : 'ssel-input-' + idSeq;

        var wrap = document.createElement('div');
        wrap.className = 'ssel';

        var input = document.createElement('input');
        input.type = 'text';
        input.id = inputId;
        input.className = 'ssel-input';
        input.setAttribute('role', 'combobox');
        input.setAttribute('aria-expanded', 'false');
        input.setAttribute('aria-autocomplete', 'list');
        input.setAttribute('aria-haspopup', 'listbox');
        input.setAttribute('aria-controls', listboxId);
        input.setAttribute('autocomplete', 'off');
        input.setAttribute('spellcheck', 'false');
        input.disabled = select.disabled;
        // Mirrors the select's own required-ness so the browser's native validation bubble
        // anchors to the visible input (first in DOM order) instead of the hidden select.
        if (select.required) input.required = true;

        var listbox = document.createElement('ul');
        listbox.className = 'ssel-listbox';
        listbox.id = listboxId;
        listbox.setAttribute('role', 'listbox');
        listbox.hidden = true;

        select.parentNode.insertBefore(wrap, select);
        wrap.appendChild(input);
        wrap.appendChild(select);
        select.classList.add('ssel-native');
        // The listbox is deliberately NOT a child of `wrap` — it's appended straight to <body>
        // (a "portal") and positioned with `position: fixed` from the input's own measured
        // coordinates in reposition() below. Every card in this app sets `overflow: hidden` (for
        // its rounded corners), which clips any absolutely-positioned descendant to the card's
        // bounds; there is no CSS-only way around that for content that must visually escape its
        // own clipped ancestor, so the listbox has to live outside that DOM subtree entirely —
        // the same technique native <select> popups and libraries like Select2 use.
        document.body.appendChild(listbox);
        // Excluded from the accessibility tree and tab order — otherwise a screen-reader or
        // keyboard user would hit this hidden select as a second, redundant combobox right after
        // the visible one. It stays perfectly functional for form submission either way.
        select.setAttribute('aria-hidden', 'true');
        select.setAttribute('tabindex', '-1');

        // Point any existing <label for="originalId"> at the new visible input instead, so
        // clicking the label focuses the searchable box rather than the now-hidden select.
        if (select.id) {
            var lbl = document.querySelector('label[for="' + select.id + '"]');
            if (lbl) lbl.setAttribute('for', inputId);
        }

        var items = optionsOf(select);
        var filtered = items.slice();
        var highlighted = -1;

        function displayText() {
            var cur = selectedOption(select);
            input.value = cur ? cur.text : '';
        }
        displayText();

        function render() {
            listbox.innerHTML = '';
            if (filtered.length === 0) {
                var empty = document.createElement('li');
                empty.className = 'ssel-option ssel-empty';
                empty.textContent = noResultsText;
                listbox.appendChild(empty);
                return;
            }
            filtered.forEach(function (item, i) {
                var li = document.createElement('li');
                li.className = 'ssel-option' + (i === highlighted ? ' ssel-highlighted' : '');
                li.id = listboxId + '-opt-' + i;
                li.setAttribute('role', 'option');
                li.setAttribute('aria-selected', item.value === select.value ? 'true' : 'false');
                li.textContent = item.text;
                if (item.disabled) {
                    li.classList.add('ssel-empty');
                } else {
                    li.addEventListener('click', function () { pick(item); });
                }
                listbox.appendChild(li);
            });
        }

        // Positions the (now body-level) listbox against the input's own live coordinates.
        // `position: fixed` is viewport-relative, so no scrollX/scrollY offset is needed — but it
        // does mean the position goes stale the moment the page scrolls or resizes, which is why
        // open() attaches listeners that keep calling this for as long as the listbox stays open.
        function reposition() {
            var r = input.getBoundingClientRect();
            var vh = window.innerHeight;
            var spaceBelow = vh - r.bottom;
            var spaceAbove = r.top;
            var openUpward = spaceBelow < 200 && spaceAbove > spaceBelow;
            listbox.style.left = r.left + 'px';
            listbox.style.width = r.width + 'px';
            if (openUpward) {
                listbox.style.top = '';
                listbox.style.bottom = (vh - r.top + 4) + 'px';
                listbox.style.maxHeight = Math.max(120, spaceAbove - 8) + 'px';
            } else {
                listbox.style.bottom = '';
                listbox.style.top = (r.bottom + 4) + 'px';
                listbox.style.maxHeight = Math.max(120, spaceBelow - 8) + 'px';
            }
        }
        function onViewportChange() { reposition(); }
        function open() {
            if (!listbox.hidden) return;
            reposition();
            listbox.hidden = false;
            input.setAttribute('aria-expanded', 'true');
            // Capture phase so this also fires for scrolling inside a nested container (e.g.
            // .table-scroll), which never bubbles a 'scroll' event to window otherwise.
            window.addEventListener('scroll', onViewportChange, true);
            window.addEventListener('resize', onViewportChange);
        }
        function close() {
            listbox.hidden = true;
            input.setAttribute('aria-expanded', 'false');
            input.removeAttribute('aria-activedescendant');
            highlighted = -1;
            window.removeEventListener('scroll', onViewportChange, true);
            window.removeEventListener('resize', onViewportChange);
        }
        function filterFrom(query) {
            var q = query.trim().toLowerCase();
            filtered = q === '' ? items.slice() : items.filter(function (it) {
                return it.text.toLowerCase().indexOf(q) !== -1;
            });
            highlighted = filtered.length > 0 ? 0 : -1;
            render();
            updateActiveDescendant();
        }
        function updateActiveDescendant() {
            if (highlighted >= 0) input.setAttribute('aria-activedescendant', listboxId + '-opt-' + highlighted);
            else input.removeAttribute('aria-activedescendant');
        }
        function moveHighlight(delta) {
            if (filtered.length === 0) return;
            highlighted = Math.max(0, Math.min(filtered.length - 1, (highlighted < 0 ? 0 : highlighted) + delta));
            render();
            updateActiveDescendant();
            var el = listbox.children[highlighted];
            if (el && el.scrollIntoView) el.scrollIntoView({ block: 'nearest' });
        }
        function pick(item) {
            select.value = item.value;
            input.value = item.text;
            close();
            select.dispatchEvent(new Event('change', { bubbles: true }));
        }

        input.addEventListener('focus', function () {
            filterFrom('');
            open();
            input.select();
        });
        input.addEventListener('input', function () {
            filterFrom(input.value);
            open();
        });
        input.addEventListener('keydown', function (e) {
            if (e.key === 'ArrowDown') { e.preventDefault(); open(); moveHighlight(1); }
            else if (e.key === 'ArrowUp') { e.preventDefault(); open(); moveHighlight(-1); }
            else if (e.key === 'Enter') {
                if (!listbox.hidden) {
                    e.preventDefault();
                    if (highlighted >= 0 && filtered[highlighted] && !filtered[highlighted].disabled) pick(filtered[highlighted]);
                }
            } else if (e.key === 'Escape') {
                if (!listbox.hidden) { e.preventDefault(); displayText(); close(); }
            } else if (e.key === 'Tab') {
                close();
            }
        });
        input.addEventListener('blur', function () {
            // Deferred so a click on a listbox option (handled below via mousedown
            // preventDefault) isn't undone by this blur firing first.
            setTimeout(function () {
                if (listbox.hidden) return;
                displayText();
                close();
            }, 0);
        });
        // Prevent the listbox from stealing focus on click, which would otherwise blur the
        // input before its 'click' listener on the option runs.
        listbox.addEventListener('mousedown', function (e) { e.preventDefault(); });

        // Keeps the overlay in sync when page script rebuilds the native select's options
        // in place (PmForm's KPI/Competency perspective-type filters do exactly this via
        // fillSelect()) — no per-call-site wiring needed on their part. The old option set is
        // gone, so an in-progress filter query no longer means anything — just show everything
        // fresh rather than try to re-apply it against unrelated new options.
        new MutationObserver(function () {
            items = optionsOf(select);
            displayText();
            if (!listbox.hidden) filterFrom('');
        }).observe(select, { childList: true });
    }

    function enhanceAll(root) {
        (root || document).querySelectorAll('select[data-searchable]').forEach(enhance);
    }

    window.SearchableSelect = { enhance: enhance, enhanceAll: enhanceAll };
    document.addEventListener('DOMContentLoaded', function () { enhanceAll(document); });
})();
