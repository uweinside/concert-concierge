using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace ConcertConcierge.TicketMaster;

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
        string? stateCode = null,
        string? countryCode = null,
        string? classificationName = null,
        int size = 20)
    {
        // Default to US if no country specified and state code is provided
        if (string.IsNullOrEmpty(countryCode) && !string.IsNullOrEmpty(stateCode))
        {
            countryCode = "US";
        }
        
        var queryParams = new List<string>
        {
            $"apikey={_apiKey}",
            $"size={size}"
        };

        if (!string.IsNullOrEmpty(keyword))
            queryParams.Add($"keyword={Uri.EscapeDataString(keyword)}");
        
        if (!string.IsNullOrEmpty(city))
            queryParams.Add($"city={Uri.EscapeDataString(city)}");
        
        if (!string.IsNullOrEmpty(stateCode))
            queryParams.Add($"stateCode={Uri.EscapeDataString(stateCode)}");
        
        if (!string.IsNullOrEmpty(countryCode))
            queryParams.Add($"countryCode={Uri.EscapeDataString(countryCode)}");
        
        if (!string.IsNullOrEmpty(classificationName))
            queryParams.Add($"classificationName={Uri.EscapeDataString(classificationName)}");

        var queryString = string.Join("&", queryParams);
        var requestUri = $"events.json?{queryString}";
        var response = await _httpClient.GetAsync(requestUri);
        
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException(
                $"Ticketmaster API request failed with status {response.StatusCode}. " +
                $"URI: {requestUri}. Response: {errorContent}");
        }
        
        return await response.Content.ReadFromJsonAsync<EventSearchResponse>();
    }
}

public class EventSearchResponse
{
    [JsonPropertyName("_embedded")]
    public EmbeddedEvents? Embedded { get; set; }
    
    [JsonPropertyName("page")]
    public PageInfo? Page { get; set; }
}

public class EmbeddedEvents
{
    [JsonPropertyName("events")]
    public List<Event>? Events { get; set; }
}

public class Event
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }
    
    [JsonPropertyName("name")]
    public string? Name { get; set; }
    
    [JsonPropertyName("url")]
    public string? Url { get; set; }
    
    [JsonPropertyName("dates")]
    public EventDates? Dates { get; set; }
    
    [JsonPropertyName("_embedded")]
    public EventEmbedded? Embedded { get; set; }
    
    [JsonPropertyName("priceRanges")]
    public List<PriceRange>? PriceRanges { get; set; }
}

public class EventDates
{
    [JsonPropertyName("start")]
    public DateStart? Start { get; set; }
}

public class DateStart
{
    [JsonPropertyName("localDate")]
    public string? LocalDate { get; set; }
    
    [JsonPropertyName("localTime")]
    public string? LocalTime { get; set; }
}

public class EventEmbedded
{
    [JsonPropertyName("venues")]
    public List<Venue>? Venues { get; set; }
}

public class Venue
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }
    
    [JsonPropertyName("city")]
    public VenueCity? City { get; set; }
    
    [JsonPropertyName("state")]
    public VenueState? State { get; set; }
    
    [JsonPropertyName("country")]
    public VenueCountry? Country { get; set; }
}

public class VenueCity
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

public class VenueState
{
    [JsonPropertyName("stateCode")]
    public string? StateCode { get; set; }
}

public class VenueCountry
{
    [JsonPropertyName("countryCode")]
    public string? CountryCode { get; set; }
}

public class PriceRange
{
    [JsonPropertyName("min")]
    public decimal Min { get; set; }
    
    [JsonPropertyName("max")]
    public decimal Max { get; set; }
    
    [JsonPropertyName("currency")]
    public string? Currency { get; set; }
}

public class PageInfo
{
    [JsonPropertyName("size")]
    public int Size { get; set; }
    
    [JsonPropertyName("totalElements")]
    public int TotalElements { get; set; }
    
    [JsonPropertyName("totalPages")]
    public int TotalPages { get; set; }
    
    [JsonPropertyName("number")]
    public int Number { get; set; }
}
