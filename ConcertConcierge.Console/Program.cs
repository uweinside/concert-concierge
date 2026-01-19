using Azure;
using Azure.AI.Agents.Persistent;
using Azure.Core;
using Azure.Identity;
using ConcertConcierge.TicketMaster;
using Microsoft.Extensions.Configuration;
using System.Text.Json;

// Load configuration from user secrets
var configuration = new ConfigurationBuilder()
    .AddUserSecrets<Program>()
    .Build();

var projectEndpoint = configuration["ProjectEndpoint"];
var modelDeploymentName = configuration["ModelDeploymentName"] ?? "gpt-4o";
var ticketMasterApiKey = configuration["TicketMasterApiKey"];

if (string.IsNullOrEmpty(projectEndpoint))
{
    Console.WriteLine("Error: ProjectEndpoint not found in user secrets.");
    Console.WriteLine("Please set it using: dotnet user-secrets set \"ProjectEndpoint\" \"<your-endpoint>\"");
    Console.WriteLine("Format: https://<AIFoundryResourceName>.services.ai.azure.com/api/projects/<ProjectName>");
    Console.WriteLine("Find this in Azure AI Foundry portal under Project Overview > Libraries > Foundry.");
    return;
}

if (string.IsNullOrEmpty(ticketMasterApiKey) || ticketMasterApiKey == "YOUR_API_KEY_HERE")
{
    Console.WriteLine("Error: TicketMasterApiKey not found or not configured in user secrets.");
    Console.WriteLine("Please set it using: dotnet user-secrets set \"TicketMasterApiKey\" \"<your-api-key>\"");
    Console.WriteLine("Get your API key from: https://developer.ticketmaster.com/");
    return;
}

// Initialize Ticketmaster client
var ticketMasterClient = new TicketMasterClient(ticketMasterApiKey);

Console.WriteLine("Concert Concierge - AI Agent");
Console.WriteLine("==============================\n");
Console.WriteLine("Authenticating with Azure CLI credentials...");
Console.WriteLine("Make sure you're logged into the same Azure account that has access to your Foundry project.");
Console.WriteLine("Use 'az login' to switch accounts if needed.\n");

// Create PersistentAgentsClient using AzureCliCredential (more reliable than DefaultAzureCredential)
PersistentAgentsClient client = new(projectEndpoint, new AzureCliCredential());

// Define the Ticketmaster search function tool
var searchEventsTool = new FunctionToolDefinition(
    name: "search_ticketmaster_events",
    description: "Search for concert and event information using the Ticketmaster API. Returns event details including name, date, venue, location, and pricing. IMPORTANT: Always provide countryCode for international cities (e.g., 'DE' for Germany, 'GB' for UK, 'CA' for Canada). Use 'US' for United States.",
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
                "description": "City name to search in (e.g., 'Seattle', 'New York', 'Munich', 'London')"
            },
            "stateCode": {
                "type": "string",
                "description": "US state code (e.g., 'WA', 'NY', 'CA'). Only use for United States cities."
            },
            "countryCode": {
                "type": "string",
                "description": "ISO 3166-1 country code (REQUIRED for international searches). Examples: 'US' for USA, 'DE' for Germany, 'GB' for UK, 'FR' for France, 'CA' for Canada, 'AU' for Australia"
            },
            "classificationName": {
                "type": "string",
                "description": "Event classification (e.g., 'Music', 'Sports', 'Arts & Theatre')"
            }
        },
        "required": []
    }
    """)
);

// Create a new agent each time
Console.WriteLine("Creating new agent with Ticketmaster search tool...");

var agentResponse = client.Administration.CreateAgent(
    model: modelDeploymentName,
    name: "Concert Concierge",
    instructions: "You are a helpful concert concierge assistant. You help users find information about concerts, artists, venues, and help them plan their concert experiences. Use the Ticketmaster search tool to find real event data when users ask about concerts, shows, or events. You can also use the code interpreter to create visualizations, PDFs, and other files for users.",
    tools: new List<ToolDefinition> { searchEventsTool, new CodeInterpreterToolDefinition() }
);

var agent = agentResponse.Value;
Console.WriteLine($"✓ Created agent with ID: {agent.Id}\n");

// Create a thread for this conversation
PersistentAgentThread thread = client.Threads.CreateThread();
Console.WriteLine($"\nStarted conversation thread: {thread.Id}");
Console.WriteLine("Chat with the Concert Concierge (type 'exit' to quit)\n");

while (true)
{
    Console.Write("You: ");
    var userInput = Console.ReadLine();
    
    if (string.IsNullOrWhiteSpace(userInput) || userInput.ToLower() == "exit")
    {
        Console.WriteLine("Goodbye!");
        break;
    }

    try
    {
        // Create user message in the thread
        client.Messages.CreateMessage(
            thread.Id,
            MessageRole.User,
            userInput);

        // Run the agent
        ThreadRun run = client.Runs.CreateRun(thread.Id, agent.Id);

        // Poll for completion
        do
        {            await Task.Delay(500);
            run = client.Runs.GetRun(thread.Id, run.Id);
        }
        while (run.Status == RunStatus.Queued || run.Status == RunStatus.InProgress);

        // Handle tool calls if the agent requires action
        if (run.Status == RunStatus.RequiresAction && run.RequiredAction is SubmitToolOutputsAction submitToolOutputsAction)
        {
            Console.WriteLine("\n🔧 Agent requesting tool execution...");
            
            var toolOutputs = new List<ToolOutput>();
            
            foreach (var toolCall in submitToolOutputsAction.ToolCalls)
            {
                if (toolCall is RequiredFunctionToolCall functionToolCall)
                {
                    Console.WriteLine($"   Calling: {functionToolCall.Name}");
                    
                    if (functionToolCall.Name == "search_ticketmaster_events")
                    {
                        try
                        {
                            // Parse the arguments
                            var functionArgs = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(functionToolCall.Arguments);
                            
                            string? keyword = functionArgs?.ContainsKey("keyword") == true && functionArgs["keyword"].ValueKind == JsonValueKind.String 
                                ? functionArgs["keyword"].GetString() : null;
                            string? city = functionArgs?.ContainsKey("city") == true && functionArgs["city"].ValueKind == JsonValueKind.String 
                                ? functionArgs["city"].GetString() : null;
                            string? stateCode = functionArgs?.ContainsKey("stateCode") == true && functionArgs["stateCode"].ValueKind == JsonValueKind.String 
                                ? functionArgs["stateCode"].GetString() : null;
                            string? countryCode = functionArgs?.ContainsKey("countryCode") == true && functionArgs["countryCode"].ValueKind == JsonValueKind.String 
                                ? functionArgs["countryCode"].GetString() : null;
                            string? classificationName = functionArgs?.ContainsKey("classificationName") == true && functionArgs["classificationName"].ValueKind == JsonValueKind.String 
                                ? functionArgs["classificationName"].GetString() : null;
                            
                            Console.WriteLine($"      Keyword: {keyword ?? "(none)"}");
                            Console.WriteLine($"      City: {city ?? "(none)"}");
                            Console.WriteLine($"      State: {stateCode ?? "(none)"}");
                            Console.WriteLine($"      Country: {countryCode ?? "(none)"}");
                            
                            // Call Ticketmaster API
                            var searchResult = await ticketMasterClient.SearchEventsAsync(
                                keyword: keyword,
                                city: city,
                                stateCode: stateCode,
                                countryCode: countryCode,
                                classificationName: classificationName
                            );
                            
                            // Format response for the agent
                            var resultJson = JsonSerializer.Serialize(searchResult, new JsonSerializerOptions 
                            { 
                                WriteIndented = false 
                            });
                            
                            Console.WriteLine($"      ✓ Found {searchResult?.Embedded?.Events?.Count ?? 0} events");
                            
                            toolOutputs.Add(new ToolOutput(functionToolCall.Id, resultJson));
                        }
                        catch (Exception ex)
                        {
                            var errorMessage = ex.Message;
                            if (ex.InnerException != null)
                            {
                                errorMessage += $" - {ex.InnerException.Message}";
                            }
                            Console.WriteLine($"      ✗ Error: {errorMessage}");
                            
                            // Provide helpful error message to agent
                            var agentError = errorMessage.Contains("400")
                                ? "Failed to search Ticketmaster API (400 Bad Request). This usually means invalid API key or malformed request. Please verify the API key is set correctly."
                                : $"Error searching Ticketmaster: {errorMessage}";
                            
                            toolOutputs.Add(new ToolOutput(functionToolCall.Id, agentError));
                        }
                    }
                }
            }
            
            // Submit tool outputs
            if (toolOutputs.Count > 0)
            {
                run = client.Runs.SubmitToolOutputsToRun(run, toolOutputs);
                
                // Continue polling after submitting tool outputs
                do
                {
                    await Task.Delay(500);
                    run = client.Runs.GetRun(thread.Id, run.Id);
                }
                while (run.Status == RunStatus.Queued || run.Status == RunStatus.InProgress);
            }
        }

        if (run.Status == RunStatus.Completed)
        {
            // Get messages and display the assistant's response
            var messages = client.Messages.GetMessages(thread.Id, order: ListSortOrder.Descending);
            var latestMessage = messages.FirstOrDefault();
            
            if (latestMessage != null && latestMessage.Role.ToString() == "assistant")
            {
                Console.Write("\nAssistant: ");
                
                // Track file IDs to download
                var fileIdsToDownload = new List<string>();
                
                foreach (var content in latestMessage.ContentItems)
                {
                    if (content is MessageTextContent textContent)
                    {
                        Console.WriteLine(textContent.Text);
                        
                        // Check for file annotations in the text
                        foreach (var annotation in textContent.Annotations)
                        {
                            if (annotation is MessageTextFilePathAnnotation filePathAnnotation)
                            {
                                fileIdsToDownload.Add(filePathAnnotation.FileId);
                            }
                        }
                    }
                    else if (content is MessageImageFileContent imageContent)
                    {
                        Console.WriteLine($"[Image File: {imageContent.FileId}]");
                        fileIdsToDownload.Add(imageContent.FileId);
                    }
                }
                Console.WriteLine();

                // Download all files found in annotations
                if (fileIdsToDownload.Count > 0)
                {
                    Console.WriteLine($"\n📎 {fileIdsToDownload.Count} file(s) generated:");
                    foreach (var fileId in fileIdsToDownload)
                    {
                        DownloadFile(client, fileId, "file");
                    }
                }
            }
        }
        else
        {
            Console.WriteLine($"\nRun failed with status: {run.Status}");
            if (run.LastError != null)
            {
                Console.WriteLine($"Error: {run.LastError.Message}\n");
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"\nError: {ex.Message}\n");
    }
}

// Clean up
client.Threads.DeleteThread(thread.Id);
client.Administration.DeleteAgent(agent.Id);
Console.WriteLine("\nConversation thread and agent deleted.");

// Helper method to download files
static void DownloadFile(PersistentAgentsClient client, string fileId, string fileType)
{
    try
    {
        // Get file metadata
        var fileInfoResponse = client.Files.GetFile(fileId);
        var fileInfo = fileInfoResponse.Value;
        
        // Extract just the filename from potential full paths like "/mnt/data/file.pdf"
        var rawFileName = fileInfo.Filename ?? $"{fileType}_{fileId}";
        var fileName = Path.GetFileName(rawFileName);
        
        Console.WriteLine($"  - {fileName}");
        
        // Download file content
        var fileContentResponse = client.Files.GetFileContent(fileId);
        var fileContent = fileContentResponse.Value;
        
        // Save to Downloads folder
        var downloadsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), 
            "Downloads");
        var filePath = Path.Combine(downloadsPath, fileName);
        
        // Handle duplicate filenames
        int counter = 1;
        while (File.Exists(filePath))
        {
            var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
            var extension = Path.GetExtension(fileName);
            filePath = Path.Combine(downloadsPath, $"{nameWithoutExt}_{counter}{extension}");
            counter++;
        }
        
        File.WriteAllBytes(filePath, fileContent.ToArray());
        Console.WriteLine($"    ✓ Downloaded to: {filePath}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"    ✗ Failed to download file {fileId}: {ex.Message}");
    }
}
