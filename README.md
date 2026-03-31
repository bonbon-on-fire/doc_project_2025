# AI Chat App - SvelteKit + ASP.NET 9.0

A modern AI chat application built with SvelteKit frontend and ASP.NET 9.0 backend.

## Contributing

Please read AGENTS.md for repository guidelines, coding standards, testing, and chat mode usage. It explains how to pick and follow the appropriate chat modes during development.

See: AGENTS.md

## 🚀 Features

- **Modern Frontend**: SvelteKit with TypeScript and Tailwind CSS
- **Robust Backend**: ASP.NET 9.0 Web API with Entity Framework Core
- **Real-time Chat**: SignalR for streaming AI responses
- **Multiple AI Providers**: Support for OpenAI, Azure OpenAI, and more
- **Authentication**: JWT-based authentication with OAuth providers
- **Responsive Design**: Mobile-first design with shadcn-svelte components
- **Chat History**: Persistent chat sessions with database storage
- **Docker Support**: Containerized development and deployment

## 📁 Project Structure

```
├── client/          # SvelteKit frontend application
├── server/          # ASP.NET 9.0 Web API backend
├── shared/          # Shared types and utilities
├── scratchpad/      # Development notes and planning
├── docs/           # Project documentation
└── docker-compose.yml
```

## 🛠 Technology Stack

### Frontend
- **SvelteKit** - Full-stack web framework
- **TypeScript** - Type-safe JavaScript
- **Tailwind CSS** - Utility-first CSS framework
- **shadcn-svelte** - Beautiful UI components
- **Vite** - Fast build tool

### Backend
- **ASP.NET 9.0** - Cross-platform web API framework
- **Entity Framework Core** - Object-relational mapping
- **SignalR** - Real-time web functionality
- **PostgreSQL/SQL Server** - Database options
- **OpenAI SDK** - AI provider integration

## 🚀 Quick Start

### Prerequisites
- Node.js 18+ and npm/pnpm
- .NET 9.0 SDK
- Docker and Docker Compose (optional)

### Development Setup

1. **Clone the repository**
   ```bash
   git clone <repository-url>
   cd DOC_Project_2025
   ```

2. **Start with Docker (Recommended)**
   ```bash
   docker-compose up -d
   ```

3. **Or run manually:**

   **Backend:**
   ```bash
   cd server
   dotnet restore
   dotnet run
   ```

   **Frontend:**
   ```bash
   cd client
   npm install
   npm run dev
   ```

4. **Environment Variables**
   Copy the example environment files and configure:
   ```bash
   cp client/.env.example client/.env.local
   cp server/appsettings.example.json server/appsettings.Development.json
   ```

### Environment Configuration

**Client (.env.local):**
```env
VITE_API_BASE_URL=http://localhost:5000
VITE_WS_BASE_URL=ws://localhost:5000
```

**Server (appsettings.Development.json):**
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=AIChat;Trusted_Connection=true;"
  },
  "OpenAI": {
    "ApiKey": "your-openai-api-key-here",
    "Model": "gpt-3.5-turbo"
  },
  "LlmApiKey": "your-llm-api-key-here",
  "Jwt": {
    "SecretKey": "your-jwt-secret",
    "Issuer": "AIChat",
    "Audience": "AIChat"
  }
}
```

**Environment Variables:**

For the LLM integration to work properly, you need to set the following environment variables:

- `LLM_API_KEY` - Your API key for the LLM provider (e.g., OpenRouter)
- `LLM_BASE_API_URL` - Base URL for the LLM provider (if different from default)

You can set these in your system environment or in a `.env` file in the server directory.

## 📚 Documentation

- [API Documentation](docs/API.md)
- [Development Guide](docs/DEVELOPMENT.md)
- [Deployment Guide](docs/DEPLOYMENT.md)
- [Architecture Overview](scratchpad/planning/architecture-plan.md)

## 🏗 Development Status

This project is currently in active development. See the [Architecture Plan](scratchpad/planning/architecture-plan.md) for detailed progress tracking.

### Current Status:
- [x] Repository structure and planning
- [ ] SvelteKit client setup
- [ ] ASP.NET server setup
- [ ] Basic chat functionality
- [ ] Authentication implementation
- [ ] Database integration

## 🤝 Contributing

1. Fork the repository
2. Create your feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add some amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

## 📄 License

This project is licensed under the MIT License - see the LICENSE file for details.

## 🙏 Acknowledgments

- Inspired by [Vercel's AI Chatbot](https://github.com/vercel/ai-chatbot)
- Based on [SvelteKit AI Chatbot](https://github.com/jianyuan/sveltekit-ai-chatbot) template
- Built with modern web technologies and best practices
