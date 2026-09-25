"""Tiny synthetic course pack used by the unit tests (no dependency on the real catalog)."""

PACK = {
    "slug": "demo-course",
    "version": 2,
    "lastReviewed": "2026-09",
    "title": "Demo Course: Testing the Studio",
    "category": "marketing",
    "level": "intermediate",
    "tools": ["Google Ads", "Meta Ads"],
    "skills": ["Budgeting", "Attribution"],
    "modules": [
        {
            "title": "Module one",
            "lessons": [
                {"slug": "intro-lesson", "title": "Intro lesson", "body": "### Hi\nno lecture here"},
                {
                    "slug": "budget-lesson",
                    "title": "Budget allocation: marginal returns",
                    "body": "### Hands-on\n\n```python\nimport numpy as np\n\ndef response(x, p):\n    return p['max'] * x\n\nprint(response(1, {'max': 2}))\n```\n",
                    "lecture": {
                        "targetMinutes": 4,
                        "pronunciations": [{"term": "ROAS", "say": "roe-as"}, {"term": "GA4", "say": "G A four"}],
                        "scenes": [
                            {"narration": "Here is a mistake. In this lecture you learn response curves and ROAS.",
                             "onScreen": "Budget allocation\n• The ROAS trap\n• Response curves", "visual": "Title card.", "seconds": 20},
                            {"narration": "Average versus marginal. Average is the past. Marginal is the next dollar.",
                             "onScreen": "Average vs marginal\n• Average: what past spend returned\n• Marginal: what the next unit returns",
                             "visual": "Two cards side by side.", "seconds": 20},
                            {"narration": "The loop has steps. Explore first. Then plan. Then verify.",
                             "onScreen": "Explore → Plan → Verify\n• Explore\n• Plan\n• Verify", "visual": "A loop diagram.", "seconds": 20},
                            {"narration": "The lesson includes a Python optimizer with a response function you can print.",
                             "onScreen": "Toy optimizer\n• Curve parameters\n• Print the response", "visual": "Code editor showing the python optimizer.", "seconds": 20},
                            {"narration": "The principle is simple. Equalize marginal returns.",
                             "onScreen": "The principle\n• Equalize marginal returns\n• Respect minimums", "visual": "Balance scale.", "seconds": 20},
                            {"narration": "Common mistakes. Moving money on GA4 averages. Try this now: sketch a curve.",
                             "onScreen": "Mistakes + try this now\n• Average moves\n• Big jumps\n• Try: sketch one curve", "visual": "Blank axes.", "seconds": 20},
                            {"narration": "Recap. Marginal, not average. Your next step: design one experiment.",
                             "onScreen": "Recap and next step\n• Marginal, not average\n• Design one experiment", "visual": "Recap slide.", "seconds": 20},
                        ],
                    },
                },
                {"slug": "next-lesson", "title": "What comes next", "body": "x"},
            ],
        }
    ],
}
