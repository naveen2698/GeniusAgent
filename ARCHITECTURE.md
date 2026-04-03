# GeniusAgent — Architecture Diagram

> AI-powered code generation agent built on .NET 10 with Clean Architecture.

---

## Solution Structure

```
GeniusAgent.sln
├── GeniusAgent.Worker          # ASP.NET Core host – HTTP API entry point
├── GeniusAgent.Application     # Orchestration / use-case layer
├── GeniusAgent.Core            # Domain models & interface contracts
└── GeniusAgent.Infrastructure  # External service integrations
```

---

## Layer Dependency Graph

```mermaid
graph TD
    Worker["<b>GeniusAgent.Worker</b><br/><i>ASP.NET Core Host</i>"]
    Application["<b>GeniusAgent.Application</b><br/><i>Orchestration</i>"]
    Core["<b>GeniusAgent.Core</b><br/><i>Domain Contracts</i>"]
    Infrastructure["<b>GeniusAgent.Infrastructure</b><br/><i>Integrations</i>"]

    Worker -->|references| Application
    Worker -->|references| Core
    Worker -->|references| Infrastructure
    Application -->|references| Core
    Infrastructure -->|references| Core

    style Worker fill:#4A90D9,color:#fff
    style Application fill:#7B68EE,color:#fff
    style Core fill:#2ECC71,color:#fff
    style Infrastructure fill:#E67E22,color:#fff
```

---

## Request Workflow

```mermaid
sequenceDiagram
    participant Client
    participant Worker as Worker<br/>POST /generate
    participant Orchestrator as FlowOrchestrator
    participant Analyzer as IRepositoryAnalyzer<br/>(VectorDbAnalyzer)
    participant LLM as ILLMService<br/>(OpenAiService)
    participant Sandbox as ICodeSandbox<br/>(DotNetCliValidator)
    participant VCS as IVersionControl<br/>(GitHubService)

    Client->>Worker: POST /generate?prompt=...
    Worker->>Orchestrator: ExecuteWorkflowAsync(prompt)

    Note over Orchestrator: Step 1 — Gather Context
    Orchestrator->>Analyzer: GetRelevantPatternsAsync(prompt)
    Analyzer-->>Orchestrator: repository patterns (from Qdrant)

    Note over Orchestrator: Step 2 — Stream Code Generation
    Orchestrator->>LLM: GenerateArtifactsStreamAsync(prompt, patterns)
    loop IAsyncEnumerable stream
        LLM-->>Orchestrator: code chunk
    end
    Orchestrator->>Orchestrator: ParseArtifacts(fullResponse)

    Note over Orchestrator: Step 3 — Validate
    Orchestrator->>Sandbox: RunBuildAndTestAsync(artifacts)
    Sandbox-->>Orchestrator: ValidationResult

    alt Build Failed
        Orchestrator->>LLM: RefineCodeAsync(code, errors)
        LLM-->>Orchestrator: corrected code
        Orchestrator->>Orchestrator: ParseArtifacts(fixedRaw)
    end

    Note over Orchestrator: Step 4 — Ship
    Orchestrator->>VCS: CreatePullRequestAsync(branch, artifacts)
    VCS-->>Orchestrator: PR created

    Orchestrator-->>Worker: complete
    Worker-->>Client: 200 OK "Agent started processing..."
```

---

## Component Detail

```mermaid
classDiagram
    direction LR

    class ILLMService {
        <<interface>>
        +GenerateArtifactsAsync(prompt, context) Task~string~
        +GenerateArtifactsStreamAsync(prompt, context) IAsyncEnumerable~string~
        +RefineCodeAsync(originalCode, errorLog) Task~string~
    }

    class IRepositoryAnalyzer {
        <<interface>>
        +GetRelevantPatternsAsync(userIntent) Task~string~
    }

    class ICodeSandbox {
        <<interface>>
        +RunBuildAndTestAsync(artifacts) Task~ValidationResult~
    }

    class IVersionControl {
        <<interface>>
        +CreatePullRequestAsync(branchName, artifacts) Task
    }

    class OpenAiService {
        -ChatClient _chatClient
        +GenerateArtifactsAsync()
        +GenerateArtifactsStreamAsync()
        +RefineCodeAsync()
    }

    class VectorDbAnalyzer {
        -QdrantClient _qdrantClient
        -EmbeddingClient _embeddingClient
        +GetRelevantPatternsAsync()
    }

    class DotNetCliValidator {
        +RunBuildAndTestAsync()
    }

    class GitHubService {
        -GitHubClient _client
        +CreatePullRequestAsync()
    }

    class FlowOrchestrator {
        +ExecuteWorkflowAsync(description)
        -ParseArtifacts(input) List~CodeArtifact~
    }

    class CodeArtifact {
        +string FileName
        +string RelativePath
        +string Content
        +ArtifactType Type
        +string FullPath
    }

    class ValidationResult {
        +bool IsSuccess
        +List~string~ Errors
    }

    class RepositoryIngestor {
        +IndexRepositoryAsync(repoPath)
        -GetEmbeddingAsync(text)
    }

    ILLMService <|.. OpenAiService : implements
    IRepositoryAnalyzer <|.. VectorDbAnalyzer : implements
    ICodeSandbox <|.. DotNetCliValidator : implements
    IVersionControl <|.. GitHubService : implements

    FlowOrchestrator --> ILLMService : uses
    FlowOrchestrator --> IRepositoryAnalyzer : uses
    FlowOrchestrator --> ICodeSandbox : uses
    FlowOrchestrator --> IVersionControl : uses
    FlowOrchestrator --> CodeArtifact : produces
    ICodeSandbox --> ValidationResult : returns
    ICodeSandbox --> CodeArtifact : consumes
    IVersionControl --> CodeArtifact : consumes

    VectorDbAnalyzer ..> RepositoryIngestor : data indexed by
```

---

## Infrastructure Integrations

```mermaid
graph LR
    subgraph External Services
        Ollama["Ollama / OpenAI<br/>LLM + Embeddings"]
        Qdrant["Qdrant<br/>Vector Database"]
        GitHub["GitHub API<br/>via Octokit"]
        DotNet["dotnet CLI<br/>Build & Test"]
    end

    subgraph GeniusAgent.Infrastructure
        OpenAiService --> Ollama
        VectorDbAnalyzer --> Qdrant
        VectorDbAnalyzer --> Ollama
        RepositoryIngestor --> Qdrant
        RepositoryIngestor --> Ollama
        GitHubService --> GitHub
        DotNetCliValidator --> DotNet
    end

    style Ollama fill:#F39C12,color:#fff
    style Qdrant fill:#8E44AD,color:#fff
    style GitHub fill:#333,color:#fff
    style DotNet fill:#512BD4,color:#fff
```

---

## DI Registration (Program.cs)

| Interface | Implementation | Lifetime |
|---|---|---|
| `ILLMService` | `OpenAiService` | Singleton |
| `IRepositoryAnalyzer` | `VectorDbAnalyzer` | Singleton |
| `ICodeSandbox` | `DotNetCliValidator` | Singleton |
| `IVersionControl` | `GitHubService` | Singleton |
| `FlowOrchestrator` | *(self)* | Scoped |

---

## Key NuGet Packages

| Package | Used By | Purpose |
|---|---|---|
| `Azure.AI.OpenAI` | Infrastructure | OpenAI-compatible chat & embedding client (Ollama) |
| `Qdrant.Client` | Infrastructure | Vector similarity search |
| `Octokit` | Infrastructure | GitHub REST API (branches, commits, PRs) |
| `Microsoft.SemanticKernel` | Application / Worker | AI orchestration primitives |
| `Hangfire.AspNetCore` | Worker | Background job scheduling |
| `Microsoft.AspNetCore.OpenApi` | Worker | OpenAPI metadata |
