using System;
using System.Text.Json;
using Hotel.ReferenceData;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Hotel.Mcp
{
    // Keep resource discovery separate from the tool catalog. Reading never books a room.
    internal static class ReferenceResources
    {
        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        private static string Rooms() => JsonSerializer.Serialize(HotelReferenceData.Rooms(), Json);
        private static string Category(string category)
        {
            try { return JsonSerializer.Serialize(HotelReferenceData.RoomType(category), Json); }
            catch (ArgumentException e) { throw new McpException(e.Message); }
        }
        private static string Cancellation() => JsonSerializer.Serialize(HotelReferenceData.Cancellation(), Json);
        public static McpServerResourceCollection Create() => new McpServerResourceCollection
        {
            McpServerResource.Create((Func<string>)Rooms, new McpServerResourceCreateOptions { Name = "RoomTypes", Description = "Hotel room capacities and bed lengths. Bed configuration by room is unspecified.", UriTemplate = HotelReferenceData.RoomTypesUri, MimeType = "application/json" }),
            McpServerResource.Create((Func<string, string>)Category, new McpServerResourceCreateOptions { Name = "RoomType", Description = "Room category information. Use a category from hotel://room-types.", UriTemplate = "hotel://room-types/{category}", MimeType = "application/json" }),
            McpServerResource.Create((Func<string>)Cancellation, new McpServerResourceCreateOptions { Name = "CancellationPolicy", Description = "Demo cancellation policy v1: 48 hours before 15:00 Berlin check-in, then one room night.", UriTemplate = HotelReferenceData.CancellationUri, MimeType = "application/json" })
        };
    }
}
