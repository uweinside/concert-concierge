# Azure AI Agents Tutorial: Building a Concert Concierge with Custom Tools

This tutorial explains how to build an AI agent using Azure AI Foundry Agents SDK with both built-in tools (Code Interpreter) and custom function tools (Ticketmaster API integration).

## Table of Contents

1. [Understanding Azure AI Agents](#understanding-azure-ai-agents)
2. [Adding Tools to an Agent](#adding-tools-to-an-agent)
3. [Code Interpreter (Python Sandbox)](#code-interpreter-python-sandbox)
4. [Custom Function Tools](#custom-function-tools)
5. [Complete Integration Example](#complete-integration-example)

---

## Understanding Azure AI Agents

Azure AI Agents are conversational AI systems that can:
- **Maintain context** across multi-turn conversations using threads
- **Use tools** to perform actions beyond simple text generation
- **Execute code** in a sandboxed Python environment
- **Call external APIs** through custom function definitions

### Key Concepts

```csharp
// 1. Client: Connects to your Azure AI Foundry project
PersistentAgentsClient client = new(projectEndpoint, new AzureCliCredential());

// 2. Agent: The AI assistant with specific capabilities and instructions
PersistentAgent agent = client.Administration.CreateAgent(/*...*/);

// 3. Thread: A conversation session that maintains message history
PersistentAgentThread thread = client.Threads.CreateThread();

// 4. Run: Executes the agent on a thread to process messages
ThreadRun run = client.Runs.CreateRun(thread.Id, agent.Id);
```

---

## Adding Tools to an Agent

Tools extend an agent's capabilities beyond text generation. Azure AI Agents supports several tool types:

### Tool Types

1. **CodeInterpreterToolDefinition** - Python code execution sandbox
2. **FileSearchToolDefinition** - Search through uploaded documents
3. **FunctionToolDefinition** - Custom function calls (your code)
4. **BingGroundingToolDefinition** - Web search capabilities

### How to Add Tools

Tools are specified when creating an agent using the `tools` parameter:

```csharp
// Example: Creating an agent with multiple tools
var agent = client.Administration.CreateAgent(
    model: "gpt-4o",                    // The AI model to use
    name: "My Assistant",               // Friendly name for your agent
    instructions: "You are a helpful assistant...",  // System prompt
    tools: new List<ToolDefinition>     // ⭐ Tools the agent can use
    {
        new CodeInterpreterToolDefinition(),     // Built-in: Python sandbox
        new FileSearchToolDefinition(),          // Built-in: Document search
        myCustomFunctionTool                     // Custom: Your function
    }
);
```

### Important Notes

- **Tools are optional** - You can create an agent with `tools: null` for text-only
- **Multiple tools** - An agent can use multiple tools in a single conversation
- **Tool selection** - The AI model decides which tool to use based on the user's request
- **Sequential execution** - The agent can call multiple tools in sequence to complete a task

---

## Code Interpreter (Python Sandbox)

The Code Interpreter tool gives your agent the ability to write and execute Python code in a secure, isolated environment.

### What Can It Do?

- **Data analysis** - Process CSV/Excel files, compute statistics
- **Visualizations** - Create charts, graphs, plots using matplotlib
- **File generation** - Create PDFs, images, spreadsheets
- **Mathematical computations** - Solve equations, perform calculations
- **Text processing** - Parse documents, extract information

### How to Enable

Simply add `CodeInterpreterToolDefinition` to your agent's tools:

```csharp
var agent = client.Administration.CreateAgent(
    model: "gpt-4o",
    name: "Data Analyst Agent",
    instructions: "You help users analyze data and create visualizations. " +
                  "Use the code interpreter to process data and generate charts.",
    tools: new List<ToolDefinition> 
    { 
        new CodeInterpreterToolDefinition()  // ⭐ Enables Python code execution
    }
);
```

### Example: Agent Creates a PDF

When a user asks "Create a PDF calendar", the agent will:

1. **Write Python code** to generate the PDF (using libraries like reportlab, fpdf)
2. **Execute the code** in the sandbox
3. **Generate a file** (e.g., `calendar.pdf`)
4. **Return file reference** in the response

### Accessing Generated Files

Files created by the Code Interpreter appear as annotations in the agent's response:

```csharp
// After the run completes, check for file outputs
var messages = client.Messages.GetMessages(thread.Id, order: ListSortOrder.Descending);
var latestMessage = messages.FirstOrDefault();

foreach (var content in latestMessage.ContentItems)
{
    if (content is MessageTextContent textContent)
    {
        // Look for file path annotations
        foreach (var annotation in textContent.Annotations)
        {
            if (annotation is MessageTextFilePathAnnotation fileAnnotation)
            {
                string fileId = fileAnnotation.FileId;
                
                // Download the file
                var fileContent = client.Files.GetFileContent(fileId);
                File.WriteAllBytes("downloaded_file.pdf", fileContent.ToArray());
            }
        }
    }
}
```

### Code Interpreter Benefits

✅ **No installation required** - Python libraries are pre-installed  
✅ **Secure execution** - Code runs in isolated sandbox  
✅ **Automatic cleanup** - Files are temporary unless explicitly downloaded  
✅ **Language agnostic** - You don't need to write Python, the AI does

---

## Custom Function Tools

Custom function tools let you extend the agent with your own capabilities by calling external APIs, databases, or business logic.

### How Function Tools Work

1. **Define the function** - Describe what it does and what parameters it accepts (JSON schema)
2. **Agent decides to call it** - Based on user input, the AI determines it needs your function
3. **You execute the function** - Your code runs and returns results
4. **Agent uses results** - The AI incorporates the output into its response

### Step 1: Define the Function Tool

A `FunctionToolDefinition` requires:
- **name** - Identifier for the function
- **description** - Tells the AI when to use it
- **parameters** - JSON Schema defining input parameters

```csharp
var searchEventsTool = new FunctionToolDefinition(
    // Function name (snake_case recommended)
    name: "search_ticketmaster_events",
    
    // Description tells the AI when to use this tool
    description: "Search for concert and event information using the Ticketmaster API. " +
                 "Returns event details including name, date, venue, location, and pricing. " +
                 "IMPORTANT: Always provide countryCode for international cities.",
    
    // Parameters using JSON Schema format
    parameters: BinaryData.FromString("""
    {
        "type": "object",
        "properties": {
            "keyword": {
                "type": "string",
                "description": "Search keyword - artist name, event name, or venue name"
            },
            "city": {
                "type": "string",
                "description": "City name to search in (e.g., 'Seattle', 'Munich')"
            },
            "countryCode": {
                "type": "string",
                "description": "ISO 3166-1 country code (e.g., 'US', 'DE', 'GB')"
            }
        },
        "required": []  // No required parameters - all are optional
    }
    """)
);
```

**Key Points:**
- Use **clear descriptions** - The AI uses these to understand when and how to call your function
- Define **all parameters** - Even optional ones should be documented
- Use **JSON Schema** - Standard format for defining data structures
- Be **specific** - More detail = better AI decision-making

### Step 2: Add Tool to Agent

```csharp
var agent = client.Administration.CreateAgent(
    model: "gpt-4o",
    name: "Concert Concierge",
    instructions: "You help users find concerts. " +
                  "Use the Ticketmaster search tool to find real event data.",
    tools: new List<ToolDefinition> 
    { 
        searchEventsTool  // ⭐ Your custom function
    }
);
```

### Step 3: Handle Function Calls in the Run Loop

When the agent wants to use your function, the run status becomes `RequiresAction`:

```csharp
// Start the agent run
ThreadRun run = client.Runs.CreateRun(thread.Id, agent.Id);

// Poll until completion or action required
do
{
    await Task.Delay(500);  // Wait 500ms between checks
    run = client.Runs.GetRun(thread.Id, run.Id);
}
while (run.Status == RunStatus.Queued || run.Status == RunStatus.InProgress);

// Check if agent needs to call a function
if (run.Status == RunStatus.RequiresAction && 
    run.RequiredAction is SubmitToolOutputsAction submitAction)
{
    var toolOutputs = new List<ToolOutput>();
    
    // Process each function call request
    foreach (var toolCall in submitAction.ToolCalls)
    {
        if (toolCall is RequiredFunctionToolCall functionCall)
        {
            // Check which function the agent wants to call
            if (functionCall.Name == "search_ticketmaster_events")
            {
                // 1. Parse the arguments the AI provided
                var args = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                    functionCall.Arguments
                );
                
                string? city = args?.ContainsKey("city") == true 
                    ? args["city"].GetString() : null;
                string? countryCode = args?.ContainsKey("countryCode") == true 
                    ? args["countryCode"].GetString() : null;
                
                // 2. Execute your function
                var searchResult = await ticketMasterClient.SearchEventsAsync(
                    city: city,
                    countryCode: countryCode
                );
                
                // 3. Convert result to JSON
                var resultJson = JsonSerializer.Serialize(searchResult);
                
                // 4. Return output to agent
                toolOutputs.Add(new ToolOutput(functionCall.Id, resultJson));
            }
        }
    }
    
    // Submit all tool outputs back to the agent
    run = client.Runs.SubmitToolOutputsToRun(run, toolOutputs);
    
    // Continue polling until agent finishes processing
    do
    {
        await Task.Delay(500);
        run = client.Runs.GetRun(thread.Id, run.Id);
    }
    while (run.Status == RunStatus.Queued || run.Status == RunStatus.InProgress);
}
```

### Step 4: Implementing the Function Logic

Create a separate class library for clean separation:

```csharp
// File: TicketMasterClient.cs
public class TicketMasterClient
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;

    public TicketMasterClient(string apiKey)
    {
        _apiKey = apiKey;
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri("https://app.ticketmaster.com/discovery/v2/")
        };
    }

    public async Task<EventSearchResponse?> SearchEventsAsync(
        string? keyword = null,
        string? city = null,
        string? countryCode = null)
    {
        // Build query parameters
        var queryParams = new List<string>
        {
            $"apikey={_apiKey}",
            $"size=20"
        };

        if (!string.IsNullOrEmpty(city))
            queryParams.Add($"city={Uri.EscapeDataString(city)}");
        
        if (!string.IsNullOrEmpty(countryCode))
            queryParams.Add($"countryCode={Uri.EscapeDataString(countryCode)}");

        // Call the API
        var response = await _httpClient.GetAsync(
            $"events.json?{string.Join("&", queryParams)}"
        );

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"API request failed: {response.StatusCode}");
        }

        // Parse and return results
        return await response.Content.ReadFromJsonAsync<EventSearchResponse>();
    }
}

// Response model
public class EventSearchResponse
{
    public EmbeddedEvents? Embedded { get; set; }
}

public class EmbeddedEvents
{
    public List<Event>? Events { get; set; }
}

public class Event
{
    public string? Name { get; set; }
    public string? Url { get; set; }
    public EventDates? Dates { get; set; }
}
```

---

## Complete Integration Example

Here's how all the pieces fit together in the Concert Concierge application:

### 1. Project Structure

```
ConcertConcierge.sln
├── ConcertConcierge.Console/          # Main application
│   └── Program.cs                     # Agent integration logic
└── ConcertConcierge.TicketMaster/     # Custom tool library
    └── TicketMasterClient.cs          # API client implementation
```

### 2. Main Application Flow

```csharp
// ========================================
// 1. SETUP: Initialize clients and tools
// ========================================

// Create Azure AI client
PersistentAgentsClient client = new(projectEndpoint, new AzureCliCredential());

// Create Ticketmaster API client
var ticketMasterClient = new TicketMasterClient(apiKey);

// Define custom function tool
var searchEventsTool = new FunctionToolDefinition(
    name: "search_ticketmaster_events",
    description: "Search Ticketmaster for events...",
    parameters: BinaryData.FromString("""{ "type": "object", ... }""")
);

// ========================================
// 2. CREATE AGENT: With multiple tools
// ========================================

var agent = client.Administration.CreateAgent(
    model: "gpt-4o",
    name: "Concert Concierge",
    instructions: "You are a concert assistant. Use Ticketmaster for events " +
                  "and code interpreter to create PDFs/visualizations.",
    tools: new List<ToolDefinition>
    {
        searchEventsTool,                      // Custom: Ticketmaster API
        new CodeInterpreterToolDefinition()    // Built-in: Python sandbox
    }
);

// ========================================
// 3. CREATE THREAD: Start conversation
// ========================================

var thread = client.Threads.CreateThread();

// ========================================
// 4. CONVERSATION LOOP
// ========================================

while (true)
{
    // Get user input
    var userInput = Console.ReadLine();
    
    // Send message to agent
    client.Messages.CreateMessage(thread.Id, MessageRole.User, userInput);
    
    // Run the agent
    var run = client.Runs.CreateRun(thread.Id, agent.Id);
    
    // ========================================
    // 5. POLLING LOOP: Wait for completion
    // ========================================
    
    do
    {
        await Task.Delay(500);
        run = client.Runs.GetRun(thread.Id, run.Id);
        
        // ========================================
        // 6. HANDLE FUNCTION CALLS
        // ========================================
        
        if (run.Status == RunStatus.RequiresAction && 
            run.RequiredAction is SubmitToolOutputsAction action)
        {
            var outputs = new List<ToolOutput>();
            
            foreach (var call in action.ToolCalls)
            {
                if (call is RequiredFunctionToolCall funcCall && 
                    funcCall.Name == "search_ticketmaster_events")
                {
                    // Parse arguments
                    var args = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                        funcCall.Arguments
                    );
                    
                    // Execute function
                    var result = await ticketMasterClient.SearchEventsAsync(
                        city: args?["city"].GetString(),
                        countryCode: args?["countryCode"].GetString()
                    );
                    
                    // Return result to agent
                    outputs.Add(new ToolOutput(
                        funcCall.Id, 
                        JsonSerializer.Serialize(result)
                    ));
                }
            }
            
            // Submit outputs and continue
            run = client.Runs.SubmitToolOutputsToRun(run, outputs);
        }
    }
    while (run.Status == RunStatus.Queued || 
           run.Status == RunStatus.InProgress ||
           run.Status == RunStatus.RequiresAction);
    
    // ========================================
    // 7. GET RESPONSE: Display agent's answer
    // ========================================
    
    if (run.Status == RunStatus.Completed)
    {
        var messages = client.Messages.GetMessages(thread.Id);
        var lastMessage = messages.FirstOrDefault();
        
        foreach (var content in lastMessage.ContentItems)
        {
            if (content is MessageTextContent text)
            {
                Console.WriteLine($"Agent: {text.Text}");
                
                // Check for generated files
                foreach (var annotation in text.Annotations)
                {
                    if (annotation is MessageTextFilePathAnnotation fileRef)
                    {
                        // Download file created by code interpreter
                        var fileContent = client.Files.GetFileContent(fileRef.FileId);
                        File.WriteAllBytes("output.pdf", fileContent.ToArray());
                        Console.WriteLine("✓ Downloaded PDF");
                    }
                }
            }
        }
    }
}
```

### 3. Example Conversation Flow

**User:** "What concerts are in Munich next month?"

1. Agent receives message
2. Agent decides to use `search_ticketmaster_events`
3. Run status → `RequiresAction`
4. Your code executes: `ticketMasterClient.SearchEventsAsync(city: "Munich", countryCode: "DE")`
5. Results returned to agent as JSON
6. Agent formats response for user
7. Run status → `Completed`
8. User sees: "Here are the concerts in Munich..."

**User:** "Can you create a PDF calendar of these?"

1. Agent receives message
2. Agent decides to use Code Interpreter
3. Agent writes Python code to create PDF using event data
4. Code executes in sandbox, generates `calendar.pdf`
5. File reference in response annotations
6. Your code downloads the PDF
7. User sees: "I've created a calendar for you" + downloaded file

---

## Best Practices

### Function Tool Design

✅ **Clear descriptions** - Help the AI understand when to use your tool  
✅ **Descriptive parameter names** - Use full words, not abbreviations  
✅ **Include examples** - Show valid input formats in descriptions  
✅ **Handle errors gracefully** - Return error messages, don't throw exceptions  
✅ **Return structured data** - JSON is easier for the AI to parse than plain text

### Code Interpreter Usage

✅ **Set expectations** - Tell users files are temporary unless downloaded  
✅ **Check annotations** - Files appear in text content annotations, not attachments  
✅ **Download immediately** - Files may be cleaned up after session ends  
✅ **Guide the agent** - Include "use code interpreter to..." in instructions

### Error Handling

```csharp
// Good error handling for custom functions
try
{
    var result = await ticketMasterClient.SearchEventsAsync(city, countryCode);
    var json = JsonSerializer.Serialize(result);
    outputs.Add(new ToolOutput(functionCall.Id, json));
}
catch (Exception ex)
{
    // Return error message that the agent can explain to the user
    var errorResponse = new { error = ex.Message };
    outputs.Add(new ToolOutput(
        functionCall.Id, 
        JsonSerializer.Serialize(errorResponse)
    ));
}
```

---

## Summary

**Adding tools to agents:**
```csharp
tools: new List<ToolDefinition> { myTool, new CodeInterpreterToolDefinition() }
```

**Code Interpreter enables:**
- Python code execution
- Data analysis & visualization
- File generation (PDF, images, etc.)

**Custom function tools require:**
1. Function definition (name, description, parameters)
2. Execution logic (your code that gets called)
3. Run loop handling (polling and submitting outputs)

**The agent orchestrates everything** - You just provide the tools, and the AI decides when and how to use them based on user requests.

---

## Next Steps

- Explore more tool types: `FileSearchToolDefinition`, `BingGroundingToolDefinition`
- Add streaming for real-time responses
- Implement conversation history persistence
- Build a UI with SignalR or WebSockets
- Deploy to Azure Container Apps

For complete code, see the Concert Concierge sample application in this repository.
