#!/usr/bin/env python3
"""Real MCP client check: initialize, list the tools, call the read-only ones.

Needs the Python SDK (`pip install mcp`) and the game bridge. It changes nothing: tools that
advance time, play a match, send an offer or answer a message are not called.

Usage: python3 tools/mcp-check.py [fm]
"""
import asyncio
import os
import sys

from mcp import ClientSession, StdioServerParameters
from mcp.client.stdio import stdio_client

SAFE = [
    "fm_status", "fm_inbox", "fm_squad", "fm_table", "fm_fixtures", "fm_calendar", "fm_report",
    "fm_news", "fm_finances", "fm_budget", "fm_board", "fm_club", "fm_facilities", "fm_happiness",
    "fm_dynamics", "fm_training", "fm_schedule", "fm_intensity", "fm_coaches", "fm_staff", "fm_scout",
    "fm_shortlist", "fm_contracts", "fm_offers", "fm_health", "fm_internationals", "fm_mentoring",
    "fm_captains", "fm_takers", "fm_penalties", "fm_tactic", "fm_depth", "fm_opponent",
    "fm_opposition", "fm_progress", "fm_world", "fm_league", "fm_supporters", "fm_numbers",
    "fm_register", "fm_responsibilities", "fm_youth", "fm_saves", "fm_targets", "fm_stages",
    "fm_prep", "fm_doctor", "fm_jobs", "fm_read", "fm_manager", "fm_advice",
]


async def main(fm: str) -> int:
    params = StdioServerParameters(command=fm, args=["mcp"])
    async with stdio_client(params) as (read, write):
        async with ClientSession(read, write) as session:
            await asyncio.wait_for(session.initialize(), 20)
            tools = {tool.name for tool in (await asyncio.wait_for(session.list_tools(), 20)).tools}
            print(f"tools: {len(tools)}")
            failed = []
            for name in SAFE:
                if name not in tools:
                    failed.append((name, "missing tool"))
                    continue
                try:
                    result = await asyncio.wait_for(session.call_tool(name, {}), 120)
                    text = "\n".join(c.text for c in result.content if c.type == "text")
                    if result.isError or not text.strip():
                        failed.append((name, (text.strip().splitlines() or ["empty"])[0][:100]))
                except Exception as error:  # noqa: BLE001 - the check must report every failure
                    failed.append((name, f"{type(error).__name__}: {error}"[:100]))
            print(f"called: {len(SAFE)}  failed: {len(failed)}")
            for name, why in failed:
                print(f"  {name} :: {why}")
            return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(asyncio.run(main(sys.argv[1] if len(sys.argv) > 1 else os.environ.get("FM", "fm"))))
