import os

# All use the SAME OutputPath pattern: ..\publish\ProjectName\
# We want: publish\  (project-local)
fixes = {
    'RimWorldAgent/RimWorldAgent.csproj': ('>..\publish\RimWorldAgent\', '>publish\'),
    'RimWorldMCP/RimWorldMCP.csproj': ('>..\publish\RimWorldMCP\', '>publish\'),
    'SimpleMspServer/SimpleMspServer.csproj': ('>..\publish\SimpleMspServer\', '>publish\'),
}
for path, (old, new) in fixes.items():
    with open(path, 'r', encoding='utf-8-sig') as f:
        c = f.read()
    c = c.replace(old, new)
    with open(path, 'w', encoding='utf-8-sig') as f:
        f.write(c)
    print(f'{path}: fixed')
