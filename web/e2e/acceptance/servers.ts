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
