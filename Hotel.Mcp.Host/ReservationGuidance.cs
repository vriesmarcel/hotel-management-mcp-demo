using System.ComponentModel;
using System.Globalization;
using Hotel.Contracts;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Hotel.Mcp.Host;

internal static class ReservationGuidance
{
    public static string Policy() => "Hotel-local dates; check-out exclusive; 1–365 nights. Quotes expire after 15 minutes and do not hold inventory. Total = (room nightly rate + food daily rate) × nights + service once. Currency is not stored. Advance cannot exceed total. Booking requires a current quote and explicit guest confirmation. Request UUIDs protect identical retries. Use synthetic guest details. Check-in belongs to Stay Operations; payments and checkout belong to Billing. Room facts: hotel://room-types (Family maximum 5; single and king beds 200 cm, queen 190 cm; room-to-bed mapping unspecified). Cancellation policy: hotel://policies/cancellation/v1. Free at or before 48 elapsed hours before 15:00 Europe/Berlin check-in; afterwards one room night excluding packages. Show the quote cancellation deadline and fee before confirmation. Accepted terms are saved with the reservation. No cancellation execution is implemented. Fixed demo authentication is not production authorization.";
    public static string PlanReservation([Description("Guest's stay requirements, without sensitive identity details.")] string requirements) =>
        "Help plan this stay: " + requirements + "\nRead hotel://reservation-policy, hotel://room-types and hotel://policies/cancellation/v1 (or the reference data included with availability). Find available rooms and packages, explain the options, then calculate a quote. Present exact dates, room, packages, total, advance and quoted cancellation deadline/fee. Ask for explicit confirmation before CreateReservation. Do not invent guest details or confirmation.";
}
