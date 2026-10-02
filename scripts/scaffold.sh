#!/usr/bin/env bash
# Creates the default-stack skeleton (ASP.NET Core 10 + EF Core/PostgreSQL + React/TypeScript).
# Project names are fixed (App.*) so Dockerfile, CI and verify.sh never need editing.
# The product name lives in docs/, not in namespaces.
#   ./scripts/scaffold.sh
# Env: OFFLINE=1 skips all downloads; NUGET_OFFLINE=1 skips only NuGet (used to test the kit itself).
# Portable: bash 3.2 (macOS) and GNU/BSD tools.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"
OFFLINE="${OFFLINE:-0}"
NUGET_OFFLINE="${NUGET_OFFLINE:-$OFFLINE}"
NR=(); [[ "$NUGET_OFFLINE" == "1" ]] && NR=(--no-restore)

if ls ./*.slnx ./*.sln >/dev/null 2>&1; then
  echo "Already scaffolded (solution exists). Nothing to do."; exit 0
fi
command -v dotnet >/dev/null || { echo "dotnet SDK 10 is required"; exit 1; }
command -v npm >/dev/null || { echo "Node.js 22+ is required"; exit 1; }
command -v perl >/dev/null || { echo "perl is required"; exit 1; }

# Insert an ItemGroup into a csproj (portable alternative to GNU sed -i with newlines).
csproj_insert() { # project-dir xml-lines
  local csproj; csproj="$(ls "$1"/*.csproj)"
  XML="$2" perl -0pi -e 's#</Project>#  <ItemGroup>\n$ENV{XML}\n  </ItemGroup>\n\n</Project>#' "$csproj"
}
# Versions float within the major so a future .NET 11 release cannot break the scaffold.
pkg() { csproj_insert "$1" "    <PackageReference Include=\"$2\" Version=\"$3\" />"; }

echo "== backend"
dotnet new sln -n App
for p in Domain Application Infrastructure; do
  dotnet new classlib -n "App.$p" -o "src/App.$p" ${NR[@]+"${NR[@]}"}
  rm -f "src/App.$p/Class1.cs"
done
dotnet new webapi -n App.Api -o src/App.Api ${NR[@]+"${NR[@]}"}
rm -f src/App.Api/App.Api.http
cat >src/App.Api/Program.cs <<'CS'
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Platform baseline (task T001): auth, tenancy, /health, problem details, rate limiting,
// security headers and SPA hosting are added here following docs/stack/dotnet-react.md.

app.Run();

public partial class Program { }
CS

for p in UnitTests IntegrationTests AcceptanceTests; do
  dotnet new xunit -n "App.$p" -o "tests/App.$p" ${NR[@]+"${NR[@]}"}
  rm -f "tests/App.$p/UnitTest1.cs"
done
csproj_insert src/App.Infrastructure '    <FrameworkReference Include="Microsoft.AspNetCore.App" />'

dotnet sln App.slnx add src/App.Domain src/App.Application src/App.Infrastructure src/App.Api \
  tests/App.UnitTests tests/App.IntegrationTests tests/App.AcceptanceTests

dotnet add src/App.Application reference src/App.Domain
dotnet add src/App.Infrastructure reference src/App.Application
dotnet add src/App.Api reference src/App.Application src/App.Infrastructure
dotnet add tests/App.UnitTests reference src/App.Domain src/App.Application src/App.Infrastructure src/App.Api
dotnet add tests/App.IntegrationTests reference src/App.Api src/App.Infrastructure
dotnet add tests/App.AcceptanceTests reference src/App.Api

pkg src/App.Infrastructure Npgsql.EntityFrameworkCore.PostgreSQL "10.0.*"
pkg src/App.Infrastructure Microsoft.AspNetCore.Identity.EntityFrameworkCore "10.0.*"
pkg src/App.Api Microsoft.EntityFrameworkCore.Design "10.0.*"
pkg src/App.Api Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore "10.0.*"
for p in IntegrationTests AcceptanceTests; do
  pkg "tests/App.$p" Microsoft.AspNetCore.Mvc.Testing "10.0.*"
  pkg "tests/App.$p" Testcontainers.PostgreSql "4.*"
done

# One factory per test project. The acceptance copy is LOCKED with the tests after approval, so the
# implementation cannot change how the acceptance suite boots the app. The integration copy is yours.
write_factory() { # namespace target-file
  cat >"$2" <<'CS'
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;

namespace __NAMESPACE__;

/// <summary>
/// Boots the real API (environment "Testing") against a real PostgreSQL:
/// a Testcontainers instance, or TEST_DB_CONNECTION when set.
/// </summary>
public sealed class AppFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly string? _external = Environment.GetEnvironmentVariable("TEST_DB_CONNECTION");
    private PostgreSqlContainer? _db;

    // https so Secure auth cookies round-trip in tests exactly as in production.
    public AppFactory() => ClientOptions.BaseAddress = new Uri("https://localhost");

    public string ConnectionString => _external ?? _db?.GetConnectionString()
        ?? throw new InvalidOperationException("Database not started.");

    public async Task InitializeAsync()
    {
        if (string.IsNullOrEmpty(_external))
        {
            _db = new PostgreSqlBuilder("postgres:17-alpine").Build();
            await _db.StartAsync();
        }
        // Read by Program.cs during startup (before WebApplicationFactory overrides apply).
        Environment.SetEnvironmentVariable("ConnectionStrings__Default", ConnectionString);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", ConnectionString);
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        if (_db is not null) await _db.DisposeAsync();
    }
}
CS
  NS="$1" perl -pi -e 's/__NAMESPACE__/$ENV{NS}/' "$2"
}
write_factory App.AcceptanceTests tests/App.AcceptanceTests/AppFactory.cs
write_factory App.IntegrationTests tests/App.IntegrationTests/AppFactory.cs

cat >tests/App.AcceptanceTests/AcceptanceCollection.cs <<'CS'
namespace App.AcceptanceTests;

/// <summary>All acceptance tests share one running API + database. Use [Collection(AcceptanceCollection.Name)].</summary>
[CollectionDefinition(Name)]
public sealed class AcceptanceCollection : ICollectionFixture<AppFactory>
{
    public const string Name = "acceptance";
}
CS

cat >tests/App.IntegrationTests/IntegrationCollection.cs <<'CS'
namespace App.IntegrationTests;

[CollectionDefinition(Name)]
public sealed class IntegrationCollection : ICollectionFixture<AppFactory>
{
    public const string Name = "integration";
}
CS

cat >tests/App.UnitTests/ArchitectureTests.cs <<'CS'
using System.Reflection;

namespace App.UnitTests;

/// <summary>Enforces the dependency rule: Domain ← Application ← Infrastructure ← Api.</summary>
public class ArchitectureTests
{
    private static string[] AppReferences(string assembly) =>
        Assembly.Load(assembly).GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(n => n.StartsWith("App.", StringComparison.Ordinal))
            .ToArray();

    [Fact]
    public void Domain_depends_on_no_other_layer() =>
        Assert.Empty(AppReferences("App.Domain"));

    [Fact]
    public void Application_depends_only_on_Domain() =>
        Assert.All(AppReferences("App.Application"), n => Assert.Equal("App.Domain", n));

    [Fact]
    public void Infrastructure_does_not_depend_on_Api() =>
        Assert.DoesNotContain("App.Api", AppReferences("App.Infrastructure"));
}
CS

if [[ "$NUGET_OFFLINE" != "1" ]]; then
  dotnet new tool-manifest --force
  dotnet tool install dotnet-ef --version "10.0.*"
  dotnet restore App.slnx
fi

echo "== frontend"
npm create vite@9 web -- --template react-ts --no-interactive </dev/null
cd web
rm -f README.md src/App.css
rm -rf src/assets public/vite.svg
if [[ "$OFFLINE" != "1" ]]; then
  npm install
  npm install @tanstack/react-query react-router
  npm install -D vitest jsdom @testing-library/react @testing-library/jest-dom @testing-library/user-event \
    @playwright/test prettier tailwindcss @tailwindcss/vite
  npx playwright install chromium || echo "WARNING: Playwright browser download failed; run 'cd web && npx playwright install chromium' later."
fi
npm pkg set scripts.typecheck="tsc -b" scripts.test="vitest run" scripts.e2e="playwright test" \
  scripts.e2e:acceptance="playwright test -c e2e/acceptance/playwright.config.ts" \
  scripts.format="prettier --write ."

cat >vite.config.ts <<'TS'
/// <reference types="vitest/config" />
import tailwindcss from '@tailwindcss/vite'
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// The API runs on :5080 in development; the browser only talks to the Vite origin (same-origin cookies).
export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    port: 5173,
    proxy: { '/api': 'http://localhost:5080', '/health': 'http://localhost:5080' },
  },
  test: {
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
    include: ['src/**/*.test.{ts,tsx}'],
  },
})
TS

# Dev servers for both Playwright configs. Kept in the locked acceptance folder so the implementation
# cannot change what the acceptance suite talks to.
mkdir -p src/test e2e/acceptance e2e/flows
cat >e2e/acceptance/servers.ts <<'TS'
// Always run Playwright from web/ (npm scripts and scripts/verify.sh do).
const webDir = process.cwd()
const db =
  process.env.TEST_DB_CONNECTION ?? 'Host=localhost;Port=5432;Database=app;Username=app;Password=app'

export const webServer = [
  {
    command: 'dotnet run --project ../src/App.Api --urls http://localhost:5080',
    cwd: webDir,
    url: 'http://localhost:5080/health',
    timeout: 180_000,
    reuseExistingServer: !process.env.CI,
    env: { ASPNETCORE_ENVIRONMENT: 'Development', ConnectionStrings__Default: db },
  },
  {
    command: 'npm run dev',
    cwd: webDir,
    url: 'http://localhost:5173',
    timeout: 60_000,
    reuseExistingServer: !process.env.CI,
  },
]
TS

# Acceptance config lives INSIDE the locked folder: the implementation cannot change which
# acceptance tests run or on which devices.
cat >e2e/acceptance/playwright.config.ts <<'TS'
import { defineConfig, devices } from '@playwright/test'
import { webServer } from './servers'

export default defineConfig({
  testDir: '.',
  fullyParallel: false,
  forbidOnly: true,
  retries: process.env.CI ? 1 : 0,
  reporter: [['list']],
  use: { baseURL: 'http://localhost:5173', trace: 'retain-on-failure', screenshot: 'only-on-failure' },
  projects: [
    { name: 'desktop', use: { ...devices['Desktop Chrome'] } },
    { name: 'mobile', use: { ...devices['Pixel 7'] } },
  ],
  webServer,
})
TS

# Your own browser flows / regression tests (not locked).
cat >playwright.config.ts <<'TS'
import { defineConfig, devices } from '@playwright/test'
import { webServer } from './e2e/acceptance/servers'

export default defineConfig({
  testDir: './e2e/flows',
  fullyParallel: false,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  reporter: [['list'], ['html', { open: 'never' }]],
  use: { baseURL: 'http://localhost:5173', trace: 'retain-on-failure', screenshot: 'only-on-failure' },
  projects: [{ name: 'flows', use: { ...devices['Desktop Chrome'] } }],
  webServer,
})
TS

cat >src/test/setup.ts <<'TS'
import '@testing-library/jest-dom/vitest'
TS
cat >src/index.css <<'CSS'
@import 'tailwindcss';
CSS
cat >src/App.tsx <<'TSX'
// Placeholder shell. The product UI is built in the build loop following docs/design.md.
export default function App() {
  return (
    <main className="mx-auto max-w-3xl p-6">
      <h1 className="text-2xl font-semibold">App</h1>
    </main>
  )
}
TSX
cat >src/App.test.tsx <<'TSX'
import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import App from './App'

describe('App', () => {
  it('renders the shell', () => {
    render(<App />)
    expect(screen.getByRole('heading', { level: 1 })).toBeInTheDocument()
  })
})
TSX
touch e2e/flows/.gitkeep
cat >.prettierignore <<'TXT'
dist
node_modules
playwright-report
test-results
e2e/acceptance
TXT
cat >.prettierrc.json <<'JSON'
{ "semi": false, "singleQuote": true, "printWidth": 100 }
JSON
if [[ "$OFFLINE" != "1" ]]; then npx prettier --write . >/dev/null; fi
cd "$ROOT"

git add -A
git commit -q -m "chore: scaffold default stack" || true
echo
echo "Scaffold done. Next: ./scripts/verify.sh --quick (expect failures until requirements and tests exist)."
