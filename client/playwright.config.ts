import { defineConfig } from '@playwright/test';

export default defineConfig({
	webServer: [
		{
			// Start backend server in Test mode on 5099
			command:
				"pwsh -NoLogo -NoProfile -Command \"cd ..\\server; dotnet build -c Debug; $env:ASPNETCORE_ENVIRONMENT='Test'; $env:ASPNETCORE_URLS='http://localhost:5099'; $env:LLM_API_KEY='DUMMY'; dotnet bin\\Debug\\net9.0\\AIChat.Server.dll\"",
			url: 'http://localhost:5099/api/health',
			reuseExistingServer: true,
			timeout: 180_000
		},
		{
			// Start client on 5173 and point it to backend 5099
			command: 'npm run dev -- --port 5173 --strict-port',
			url: 'http://localhost:5173',
			reuseExistingServer: true,
			timeout: 120_000,
			env: {
				PUBLIC_API_BASE_URL: 'http://localhost:5099'
			}
		}
	],
	use: {
		baseURL: 'http://localhost:5173'
	},
	testDir: 'e2e'
});
