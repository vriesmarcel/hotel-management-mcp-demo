"""Local demo MCP adapter for sessions without native hotel tools. Python 3.11+."""
import argparse
import json
import sys
import urllib.error
import urllib.request
import uuid

ENDPOINT = "http://127.0.0.1:5057/mcp/reservations"
HEADERS = {"Authorization": "Bearer 123456", "Accept": "application/json, text/event-stream",
           "Content-Type": "application/json", "MCP-Protocol-Version": "2025-11-25"}
FIELDS = {
    "GetAvailableRooms": ({"checkIn", "checkOut"}, set()),
    "GetPackages": (set(), set()),
    "CalculateQuote": ({"roomId", "checkIn", "checkOut"}, {"foodPackageId", "servicePackageId", "advance"}),
    "CreateReservation": ({"quoteId", "guestName", "address", "phone", "nationalId", "advance", "requestId", "confirmed"}, set()),
    "GetReservation": ({"reservationId"}, set()),
}


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None  # Never forward the demo token or guest details elsewhere.


class ToolFailure(Exception):
    pass


class RpcFailure(Exception):
    pass


def decode_response(text, request_id):
    try:
        messages = [json.loads(text)]
    except json.JSONDecodeError:
        messages = []
        for event in text.replace("\r\n", "\n").split("\n\n"):
            data = "\n".join(line[5:].lstrip(" ") for line in event.splitlines() if line.startswith("data:"))
            if data:
                messages.append(json.loads(data))
    for message in messages:
        if message.get("id") == request_id:
            if "error" in message:
                raise RpcFailure(message["error"].get("message", "MCP request failed."))
            return message["result"]
    raise ValueError("No matching MCP response received.")


def rpc(method, parameters, request_id):
    body = json.dumps({"jsonrpc": "2.0", "id": request_id, "method": method, "params": parameters}, allow_nan=False).encode()
    request = urllib.request.Request(ENDPOINT, data=body, headers=HEADERS, method="POST")
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}), NoRedirect())
    with opener.open(request, timeout=300) as response:
        return decode_response(response.read().decode("utf-8"), request_id)


def validate(tool, arguments):
    if tool not in FIELDS or not isinstance(arguments, dict):
        raise ValueError("Use an allowed Reservations tool and a JSON arguments object.")
    required, optional = FIELDS[tool]
    if required - arguments.keys() or arguments.keys() - required - optional:
        raise ValueError("Missing or unknown tool arguments; check the tool contract.")
    if tool == "CreateReservation":
        if arguments["confirmed"] is not True:
            raise ValueError("CreateReservation requires explicit user confirmation and confirmed=true.")
        uuid.UUID(arguments["requestId"])
        uuid.UUID(arguments["quoteId"])


def invoke(tool, arguments):
    validate(tool, arguments)
    result = rpc("tools/call", {"name": tool, "arguments": arguments}, 2)
    if result.get("isError"):
        raise ToolFailure("\n".join(block.get("text", "") for block in result.get("content", []) if block.get("type") == "text"))
    if "structuredContent" in result:
        return result["structuredContent"]
    blocks = [block["text"] for block in result.get("content", []) if block.get("type") == "text"]
    return json.loads(blocks[0]) if blocks else result


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("tool", choices=[*FIELDS, "policy", "room-info", "cancellation-policy", "catalog", "new-request-id"])
    args = parser.parse_args(argv)
    arguments = {}
    mutation_started = False
    try:
        if args.tool == "new-request-id":
            print(str(uuid.uuid4()))
            return 0
        if args.tool in FIELDS:
            arguments = json.load(sys.stdin)
            validate(args.tool, arguments)
        rpc("initialize", {"protocolVersion": "2025-11-25", "capabilities": {},
                           "clientInfo": {"name": "hotel-booking-skill", "version": "1.0.0"}}, 1)
        if args.tool in ("policy", "room-info", "cancellation-policy"):
            uri = {"policy": "hotel://reservation-policy", "room-info": "hotel://room-types", "cancellation-policy": "hotel://policies/cancellation/v1"}[args.tool]
            result = rpc("resources/read", {"uri": uri}, 2)
        elif args.tool == "catalog":
            result = rpc("tools/list", {}, 2)
        else:
            mutation_started = args.tool == "CreateReservation"
            result = invoke(args.tool, arguments)
        output = {"ok": True, "result": result}
        if "requestId" in arguments:
            output["requestId"] = arguments["requestId"]
        print(json.dumps(output, ensure_ascii=False))
        return 0
    except (ValueError, TypeError, KeyError, ToolFailure, RpcFailure, urllib.error.URLError, OSError) as error:
        # This host's explicit tool errors reject the transactional operation.
        # A transport/protocol failure can follow a commit: preserve the request key.
        outcome = "rejected" if isinstance(error, ToolFailure) else "unknown" if mutation_started else "not_booked"
        output = {"ok": False, "outcome": outcome,
                  "error": str(error) if isinstance(error, (ValueError, ToolFailure, RpcFailure)) else "Local hotel MCP request failed; check the running host."}
        if isinstance(arguments, dict) and "requestId" in arguments:
            output["requestId"] = arguments["requestId"]
        print(json.dumps(output))
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
