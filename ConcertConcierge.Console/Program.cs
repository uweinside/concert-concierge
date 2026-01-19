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
                foreach (var content in latestMessage.ContentItems)
                {
                    if (content is MessageTextContent textContent)
                    {
                        Console.WriteLine(textContent.Text);
                    }
                }
                Console.WriteLine();
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
