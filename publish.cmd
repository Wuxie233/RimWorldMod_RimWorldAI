@echo off
echo === RimWorldAI 发布 ===

echo 构建项目...
dotnet build RimWorldAI.sln -c Release -v q

echo 合并输出到 publish/...
rmdir /s /q publish 2>/dev/null
mkdir publish

for %%p in (SimpleMspServer RimWorldMCP RimWorldAgent) do (
    if exist "%%p\publish" (
        mkdir publish\%%p 2>/dev/null
        xcopy "%%p\publish\*" "publish\%%p\" /E /Y /Q >/dev/null
        echo   %%p: done
    )
)

echo === 发布完成 ===
