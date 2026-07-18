# Management window IA prototype

Throwaway browser prototype for the Wayfinder ticket **Prototype the Management window information architecture**.

Run from the repository root:

```powershell
py -m http.server 8765 -d prototypes/management-ia-prototype
```

Then open `http://localhost:8765/?variant=A`. Switch variants with the floating controls or the left/right arrow keys.

- `A` — chosen direction: History and Dictionary at the top; Settings and Quit at the bottom. Settings replaces the main navigation with its own pages and a Back action.
- `B` — broad task-oriented top navigation; Quit and rare commands live in an app menu; the Dictionary is shown empty.
- `C` — compact left-side hub; model management is a first-class area; Quit lives inside the app menu.

This prototype is read-only and intentionally has no persistence, tests, or production abstractions.
