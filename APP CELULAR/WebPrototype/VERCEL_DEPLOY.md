# CATCHAT Web on Vercel

The Vercel project is a static Blazor WebAssembly deployment from this Git repository. It does not run the ASP.NET Core host in `WebPrototype.csproj`.

## Vercel project settings

- Git repository: `wendelxinxa-cell/APP_CELULAR`
- Root Directory: `APP CELULAR/WebPrototype`
- Framework Preset: Other
- Build Command: `bash vercel-build.sh`
- Output Directory: `Client/publish/wwwroot`
- Production Branch: keep the repository's configured default branch; use Preview deployments for feature branches.

The checked-in `vercel.json` forwards browser API calls to the Vercel API Preview branch and routes client-side Blazor paths back to `index.html`.

## Preview configuration

The build reads the browser-safe Supabase settings from `appsettings.json`; Vercel Preview environment variables can override them:

- `Supabase__Url`
- `Supabase__PublishableKey`
- `Supabase__Bucket`
- `Supabase__EmpresaId`

The publishable key is public browser configuration. Never put a Supabase secret or `service_role` key in this project.

In Supabase Auth, add the Vercel Preview URL pattern to the allowed redirect URLs so password recovery and email confirmation return to the site. The app redirects back to the current site origin.

## Request flow

- Supabase Auth, chat RPCs, Realtime, and Storage are called from the browser to Supabase.
- `/api/chat/contacts` is rewritten to `POST /api/conversas/contatos-web` on the Vercel API.
- `/api/administracao/*` is rewritten to the matching Vercel API route.
- The rewrites keep API calls on the web site's origin, so the browser does not need cross-origin access to the API.

The Vercel API target in `vercel.json` is the Preview branch alias for `codex/vercel-session-persistence`. Update that target if the API project or branch alias changes.
