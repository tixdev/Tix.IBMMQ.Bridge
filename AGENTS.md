# AGENTS.md

## Project Description
This repository may contain different types of .NET applications, such as Angular web applications, console apps, APIs, or background services. Projects are developed using ASP.NET Core 8.0+, C# 12+, and Entity Framework Core where applicable. The codebase follows best practices including strong separation of concerns, domain-driven design, and efficient workflows with Visual Studio or JetBrains Rider.NET Core 8.0+, C# 12+, and Entity Framework Core. The code follows best practices for component-based UI, domain-driven design principles, strong separation of concerns, and efficient development workflows using Visual Studio Enterprise and Cursor AI.

## Tech Stack
- **Frontend**: Angular 16+ (standalone components, SCAM pattern)
- **Backend**: ASP.NET Core 8.0+
- **Database**: SQL Server with EF Core
- **IDE**: Visual Studio Enterprise 2022+ or JetBrains Rider 2023.1+
- **AI Tooling**: Cursor AI (for editing, refactoring, and suggestions)
- **State Management**: Built-in
- **Validation**: FluentValidation / DataAnnotations
- **Caching**: IMemoryCache / SQL Server Cache
- **Testing**: xUnit with Moq

## Development Requirements
- Visual Studio Enterprise 2022 (17.8+) or JetBrains Rider 2023.1+
- .NET 8.0 SDK+
- SQL Server Developer Edition
- Node.js 18+ (if client-side bundling is needed)
- ensure dotnet sdk is installed with sudo apt-get update && sudo apt-get install -y dotnet-sdk-8.0
- if necessary use sudo usermod -aG docker <nome_utente> and sudo chmod 666 /var/run/docker.sock for use testcontainer or run container nested


## Project Structure Guidelines
- `Components/` — Angular components organized using SCAM pattern and standalone modules
- `Data/` — EF DbContext, Models, Repositories
- `Services/` — Business services implementing logic and state handling
- `ViewModels/` — View-specific models
- `Validators/` — FluentValidation classes
- `wwwroot/` — Static assets (CSS, JS, Images)
- `Tests/` — Unit and integration test projects

## Code Style and Conventions
- **PascalCase**: public members, methods, classes, interfaces (prefixed with `I`)
- **camelCase**: local variables, private fields, parameters
- **UPPER_CASE**: constants
- **kebab-case**: HTML/CSS class names

## Pull Request Expectations
- One feature/bugfix per PR
- Must include related issue ID
- If UI is affected, include screenshots
- Run tests (`dotnet test`) and linting before submitting

## Commands and Quality Checks
- Build: `dotnet build`
- Run: `dotnet run`
- Restore: `dotnet restore`
- Test: `dotnet test`
- Format: `dotnet format`

## Testing
- Use Moq for mocking dependencies
- Prefer xUnit unless explicitly required otherwise
- Component testing with `bunit`
- Integration tests placed in `Tests/IntegrationTests`

## Error Handling
- Use Angular's `ErrorHandler` and global error boundary services for UI-level error capture
- Log exceptions using Serilog
- Use `try/catch` + JSInterop to report UI-side exceptions

## Dependency Injection
- All services must be registered in `Program.cs`
- Prefer `AddScoped` for domain services
- Use `IUserService`, `ICacheService`, etc. instead of concrete types

## State Management
- Simple apps: built-in cascading values/state containers
- Complex state: use `@ngrx/component-store` or `@ngrx/store`

## AI Tooling
- Cursor AI is used for editing and refactoring
- This file is meant to guide Codex/Cursor agents when generating code
- AI agents should:
  - Follow the directory and naming structure
  - Add/update FluentValidation validators for ViewModels
  - Ensure all Angular components follow lifecycle best practices (`ngOnInit`, `ngOnDestroy`, etc.)
  - Add tests for every new service method or component logic
  - Prefer async/await and dependency injection

## Swagger & API Docs
- API projects must expose OpenAPI via Swagger in dev
- Swagger docs generated from XML comments

## Security Guidelines
- Prefer Identity or JWT for authentication
- Enforce HTTPS
- Validate all user input
- Apply Authorization policies where applicable

---

_This AGENTS.md serves as onboarding and guidance for AI agents interacting with the repository to ensure consistency, correctness, and alignment with project standards._
