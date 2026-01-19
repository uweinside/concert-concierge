using Azure;
using Azure.AI.Agents.Persistent;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Configuration;

// Load configuration from user secrets
var configuration = new ConfigurationBuilder()
    .AddUserSecrets<Program>()
    .Build();

var projectEndpoint = configuration["ProjectEndpoint"];
var modelDeploymentName = configuration["ModelDeploymentName"] ?? "gpt-4o";
var agentId = configuration["AgentId"];

if (string.IsNullOrEmpty(projectEndpoint))
{
    Console.WriteLine("Error: ProjectEndpoint not found in user secrets.");
    Console.WriteLine("Please set it using: dotnet user-secrets set \"ProjectEndpoint\" \"<your-endpoint>\"");
    Console.WriteLine("Format: https://<AIFoundryResourceName>.services.ai.azure.com/api/projects/<ProjectName>");
    Console.WriteLine("Find this in Azure AI Foundry portal under Project Overview > Libraries > Foundry.");
    return;
}

Console.WriteLine("Concert Concierge - AI Agent");
Console.WriteLine("==============================\n");
Console.WriteLine("Authenticating with Azure CLI credentials...");
Console.WriteLine("Make sure you're logged into the same Azure account that has access to your Foundry project.");
Console.WriteLine("Use 'az login' to switch accounts if needed.\n");

// Create PersistentAgentsClient using AzureCliCredential (more reliable than DefaultAzureCredential)
PersistentAgentsClient client = new(projectEndpoint, new AzureCliCredential());

// Get or create an agent
PersistentAgent agent;
if (!string.IsNullOrEmpty(agentId))
{
    Console.WriteLine($"Using existing agent: {agentId}");
    agent = client.Administration.GetAgent(agentId);
}
else
{
    Console.WriteLine("Creating new agent...");
    agent = client.Administration.CreateAgent(
        model: modelDeploymentName,
        name: "Concert Concierge",
        instructions: "You are a helpful concert concierge assistant. You help users find information about concerts, artists, venues, and help them plan their concert experiences."
    );
    Console.WriteLine($"Created agent with ID: {agent.Id}");
    Console.WriteLine($"Save this ID with: dotnet user-secrets set \"AgentId\" \"{agent.Id}\"");
}

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
Console.WriteLine("\nConversation thread deleted.");

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
