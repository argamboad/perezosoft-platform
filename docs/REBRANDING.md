# Rebranding checklist

`dotnet new perezosoft-app -n <Brand>` already wrote your name into every identifier: the solution, the projects and
namespaces, the native app's id (`com.<brand>.app`) and OAuth callback scheme (`<brand>`), the email sender name, the
email brand's name, the Postman files, the CI identifiers (NEW_APP_GUIDE Phase 2). What is left is what no rename can
do: **your brand assets, the brand options, and your tenant noun.** The packages keep their names
(`Perezosoft.Platform.*`, `using Perezosoft.Infrastructure…`) in every app, by design (ADR-034) — they are not brand
copy, and nothing here touches them.

> **Don't stop at the UI.** The transactional emails (sign-in code, magic link, invitation, notifications) are
> rendered by the platform package, with their own copy of the name, logo, colours and tagline: email HTML can't share
> the app's CSS or static assets. You brand them in one block of options (§2) — skip it and every mail your users get
> still wears the platform's logo and palette.

(A clone of the platform — vuelto, jigger-jot — converts to the packages instead of rebranding again. The clone-era
checklist, with its mechanical rename of 545 files, is in this file's history before Pkg 6.5.)

## 1. Brand assets — replace the files (keep the filenames to avoid touching references)

**Logos & images.** In-app UI (the Shared.Ui Razor class library — used by web + desktop + mobile):
- `src/Shared.Ui/wwwroot/brand/{icon_light.svg, icon_light_1024.png, lockup_light.svg, lockup_light_1520.png, lockup_dark.svg, lockup_dark_1520.png}` —
  the lockup SVGs also carry the **tagline** ("Lazy reputation. Efficient engineering." until you change it).
- **The UI references the PNG lockups, not the SVGs.** Webfonts don't load inside an `<img>`-embedded
  SVG, so an SVG lockup's wordmark silently falls back to Helvetica/Arial. Keep the SVGs as the
  editable source, render the PNGs from them, and point `Login.razor`/`Home.razor` at the PNGs.

Web host chrome:
- `src/Web/wwwroot/{favicon.ico, favicon.svg, favicon.png, apple_touch_180.png, og_image_1200x630.png}`
- PWA icon set (ready for a future manifest): `src/Web/wwwroot/{icon-192.png, icon-512.png, icon-maskable-512.png}`

Native launcher icon + splash (MAUI):
- `src/Maui/Resources/AppIcon/{appicon.svg, appiconfg.svg}` — background layer + foreground mark
  (foreground sized to the Android adaptive-icon safe zone, ~61% of canvas)
- `src/Maui/Resources/Splash/splash.svg` + the `Color` attrs on `MauiIcon`/`MauiSplashScreen` in
  `src/Maui/<Brand>.Maui.csproj` (brand background colour behind icon + splash)

**Rasters are generated, never hand-exported.** The platform's `docs/brand/build_assets.py` regenerates every PNG
above, `favicon.ico` and the store/marketing set from the SVG sources with a headless Chromium (so lockup wordmarks
carry the real webfonts) + Pillow. The starter does not ship it: copy it into your `docs/brand/`, set the ground colours
in its config block, swap the SVGs, run it. It also writes the email logo to the platform's path
(`src/Infrastructure/Email/Assets/logo.png`); in an app, take that PNG into your host as an embedded resource for
`options.Brand.Logo` (§2).

**Colour palette — derive it from your logo.** The palette is semantic tokens, single-sourced for web **and** all
native shells:
- **`src/Shared.Ui/wwwroot/css/app.css`** — the `:root` vars (`--bs-primary`/`--bs-primary-rgb`,
  `--brand-accent`, `--brand-dark`, `--brand-accent-light`, `--bs-link-color`/`--bs-link-hover-color`,
  `--app-bg`, `--app-border`). One file; both hosts load it via
  `_content/<Brand>.Shared.Ui/css/app.css`. Typical derivation from a logo: primary = the logo's
  dominant mid tone, dark = its darkest shade (hover/active), accent(-light) = supporting tones,
  bg/border = a near-white and a soft border tinted toward the primary. Check WCAG contrast for
  white text on `--bs-primary`.
- **The Android system bars** — `src/Maui/Platforms/Android/Resources/values/colors.xml` (the theme's
  `colorPrimary`/`colorAccent` = `--bs-primary`, `colorPrimaryDark` = `--brand-dark`) and
  `SystemBarColors` in `src/Maui/Platforms/Android/SystemBarColors.cs`, whose `Light`/`Dark` must
  equal the header's background in each theme (here `--bs-primary` in both; if your header takes a
  different surface per theme, use those) and `LightGround`/`DarkGround` app.css's `--app-bg` in each
  theme. The status bar is drawn by the OS above the header (or above the bare page on the sign-in
  screens), and the page paints it through `SystemBarThemeSync`. Before 2026-09-16 the template hard-coded its sage
  in `MainActivity` in every theme and no rebrand step named it, so downstream Android apps (JiggerJot, y-el-vuelto)
  wore it.
- **The email palette** — `options.Brand.Colors` (§2).

**Marketing & store submission** (not shipped in the app): LinkedIn banner and logo, `app_store_icon_1024.png`,
`play_store_icon_512.png`, `android_adaptive_foreground_432.png` — `build_assets.py` writes them into `docs/brand/`
for the release checklist (`NEW_APP_GUIDE.md` Phase 9; ADR-024).

## 2. The brand options — one block in `AppComposition.ConfigurePlatform`

The mails are the package's (`BrandedEmail`, Pkg 3.9); an app brands them through `PlatformOptions.Brand` and never
edits the package. The template already set the name; the rest is yours:

```csharp
// src/Host/AppComposition.cs
public static void ConfigurePlatform(PlatformOptions options)
{
    options.Brand.Name = "<Brand>";                      // title, logo alt, header, footer — the template set it
    options.Brand.Tagline = "<your tagline>";
    options.Brand.Colors.Primary = "#1f6f8b";            // and PrimaryDark, Accent, AccentLight, Surface, Border, Ink, Muted
    options.Brand.Logo = EmbeddedPng("Email.logo.png");  // PNG bytes, ~128px square
    options.Brand.Strings.Add(new ResourceManager("<Brand>.Host.Email.EmailStrings", typeof(AppComposition).Assembly));
}
```

- **The logo is a PNG**, CID-embedded: mail clients strip SVG and block data-URIs, and the host refuses anything else
  at startup. Ship it as an embedded resource of your host.
- **The colours are `#rrggbb`**: email HTML can't use CSS variables, so the mails take the values directly; anything
  but a hex colour stops the host at startup.
- **`Brand.Strings`** takes your own resx holding only the keys you change (the platform's mention its name in the
  preheaders and the invitation body); they win over the platform's one culture at a time, so a key you override in
  English only still reaches a Spanish reader in the platform's Spanish. Override each key in every language you ship.
- **The name follows everywhere else on its own:** the MFA issuer (what authenticator apps display for enrolled
  accounts), the OpenAPI title ("<Brand> API") and the telemetry service name (`options.Brand.ServiceName`, the name
  without spaces + `.Api` unless you set it). Changing the issuer does not touch already-enrolled authenticators:
  they keep the old label until the user re-enrolls.
- **The sender's display name is configuration, not brand:** `Email:Smtp:FromName` in `src/Host/appsettings.json`
  (the template set it to your name), overridden by `Email__Smtp__FromName` in `.env` or the host's environment.

**Mails of your own design.** When the options cannot say it — your own layout, a webfont, more than one image, an
illustration per mail — replace the renderer instead (Pkg 7.y): implement `IBrandedEmail` (`Otp`, `MagicLink`,
`Invitation`, `Notification`, each returning the subject, the HTML and its inline images) and register it in
`AppComposition.AddAppServices` with `services.AddSingleton<IBrandedEmail, YourMails>()`. It runs after the platform's
registrations, so every platform mail goes through yours. Keep the email rules your renderer now owns: inline styles,
images as CID PNGs, a font embedded rather than linked, every language you ship.

**Product copy.** The brand name and the product's words live in `src/Shared.Ui/Resources/AppStrings.resx` (English)
and `AppStrings.es.resx` (Spanish): read both for copy that describes the platform rather than your product (the
Home page's hero, the sign-in page), in every language you ship. See `docs/LOCALIZATION.md`.

## 3. Your tenant noun

**The tenant's name is "household" (ADR-032), and the API keeps it** (ADR-034 point 6): the packaged routes
(`api/household`), `HouseholdController` and `HouseholdInvitationsController` say household in every app. If your
tenant is a team, a company or a classroom, rename **the seed** — the word your users see — and never the packaged API:

- the `Household_*` resource keys and their copy in both `.resx` files,
- the page (`src/Shared.Ui/Pages/Household.razor`, its route `/household`) and the header's link,
- the `nav-household` test id and the journeys that use it (`tests/E2E.Tests`), and the page's component tests.

A rename pair (`Household` → `Team`, `household` → `team`) over `src/Shared.Ui`, `tests/Ui.Tests` and `tests/E2E.Tests`
does it; leave `src/Host` and the Postman collection alone (they call the API). If your tenant *is* a household, there
is nothing to do.

## 4. The native app's identity

- **`ApplicationId`** — `com.<brand>.app` in `src/Maui/<Brand>.Maui.csproj`. It is the store identity: settle it
  before the first release, because it can never change after. If you change it, move the two places that name it
  with it: the `PKG` fallback in `tests/native-smoke-android/smoke.js` and the keychain group in
  `src/Maui/Platforms/MacCatalyst/Entitlements.plist` (a group that doesn't match the bundle id means SecureStorage
  silently fails on Catalyst under real signing). CI finds the app by the `ApplicationId` itself.
- **`<ApplicationTitle>`** next to it — the installed app's display name on every native platform.
- **The OAuth callback scheme** (`<brand>`) agrees in `src/Maui/MauiProgram.cs`, the Android callback activity, both
  Apple `Info.plist` files and `Auth:Native:CallbackScheme` in `src/Host/appsettings.json`; register
  `{scheme}://auth` and your `signin-*` redirect URIs with the provider consoles.

## Verify the brand

- No placeholder survived the template: `git grep -i appbrand` returns nothing.
- No platform brand copy is left: a search for the old tagline (`Lazy reputation. Efficient engineering.`) and for
  the platform's palette constants (`#465d4d`, `#6b8a72`) returns nothing.
- The rasters came from your SVGs through `build_assets.py`, not from a hand export.
- The build and the tests pass exactly as `ci.yml` runs them.
- Web and desktop show the new brand, **and** a test email (trigger an OTP or invite) arrives with the new logo,
  colours, name, and tagline.
