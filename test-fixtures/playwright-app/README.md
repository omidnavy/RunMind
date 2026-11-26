# RunMind Playwright browser-test fixture

A dependency-free local HTML application for testing RunMind's Playwright navigation, page inspection, accessibility snapshot, interactive controls, console/network diagnostics, and screenshots. It makes no external network calls.

## Run it

Select this folder as the native execution workspace in RunMind. Install Playwright and Chromium in this fixture folder once:

```powershell
npm install --save-dev playwright
npx playwright install chromium
```

Then use the RunMind development-server tool to discover and start `dev`, or run it manually:

```powershell
npm run dev
```

The app is served at `http://127.0.0.1:4320/`; the readiness endpoint is `http://127.0.0.1:4320/health`. `dev-alt` starts a second instance on port 4321.

The page contains a labelled form, a priority select, submit and reset buttons, navigation landmarks, an `aria-live` status region, and a results list. Submitting the form updates the page without contacting a backend.
