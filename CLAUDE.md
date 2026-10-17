@AGENTS.md

## Tray app conventions

This is a tray-only WinUI 3 app built from the `winui-tray-app` skill scaffold on top of `winapp new`. Load that skill before changing app lifetime, the flyout, the tray icon, polling, settings, or secrets.

- Launch with `winapp run` (or `dotnet run`); never register the package by hand. Use `winapp run --clean` to test first-run behavior.
- `App.xaml.cs` owns the tray icon, single instance, and the only exit path (the Exit menu). There is no main window.
- The flyout (`Views/TrayFlyoutWindow`) is hidden on dismiss and closed after a minute hidden. Anything it starts, it must stop in `FlyoutPage.OnHidden` / `Dispose`.
- Network work runs only while visible, through `Services/ForegroundPoller`. The one exception is background work the tray icon itself displays, and that needs a documented reason.
- The app has no keys or tokens of its own. `Services/ClaudeUsageService` reads Claude Code's sign-in (`%USERPROFILE%\.claude\.credentials.json`, or `%CLAUDE_CONFIG_DIR%`) for each request and sends it only to api.anthropic.com. Never write that file, renew its token, copy it, or log it: the refresh token rotates, so renewing it here would sign Claude Code out.
- `/api/oauth/usage` is undocumented. `Usage/UsageParser` reads both shapes it has had (a `limits` array, and the older flat `five_hour` / `seven_day` keys); when it changes, add a recorded sample to the tests first.
- The repo is MIT licensed (`LICENSE`). Keep `README.md` in step with the features.
- `Usage/` is pure logic (parsing, pace math, formatting) with no WinRT dependency. `tests/TraiUsage.Tests` links every file in it, so keep it WinRT-free. Run `dotnet test tests/TraiUsage.Tests`.
