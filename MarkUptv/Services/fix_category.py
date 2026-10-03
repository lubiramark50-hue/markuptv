import re

path = r"C:\Users\hp\source\repos\MarkUptv\MarkUptv\Services\AiTvService.cs"
with open(path, "r", encoding="utf-8") as f:
    content = f.read()

# It currently looks like: c.Category?.Contains("sport", StringComparison.OrdinalIgnoreCase)
# We want to replace it with: c.Category?.Contains("sport", StringComparison.OrdinalIgnoreCase) == true

# First let's match c.Category?.Contains(...) where it's used in a lambda or if statement returning a bool
content = re.sub(
    r'(c\.Category\?\.Contains\([^)]+\))(?!\s*==\s*true)',
    r'\1 == true',
    content
)

with open(path, "w", encoding="utf-8") as f:
    f.write(content)

print("Replaced Category?.Contains with == true")
