$lines = Get-Content AIChat.Server.csproj
$output = @()
$skip = $false
$skipCount = 0

for ($i = 0; $i -lt $lines.Count; $i++) {
    $line = $lines[$i]
    
    # Start skipping at line 68
    if ($i -eq 67) { # 0-indexed, so line 68 is index 67
        $skip = $true
        # Add replacement content
        $output += "  <!-- OpenAI Provider Models for TestMode - minimal LmDotnetTools dependency -->"
        $output += "  <ItemGroup Condition=`"Exists('../../submodules/LmDotnetTools/src/OpenAiProvider/AchieveAi.LmDotnetTools.OpenAiProvider.csproj')`">"
        $output += "    <ProjectReference"
        $output += "      Include=`"../../submodules/LmDotnetTools/src/OpenAiProvider/AchieveAi.LmDotnetTools.OpenAiProvider.csproj`" />"
        $output += "  </ItemGroup>"
        $output += "  <ItemGroup Condition=`"!Exists('../../submodules/LmDotnetTools/src/OpenAiProvider/AchieveAi.LmDotnetTools.OpenAiProvider.csproj')`">"
        $output += "    <PackageReference Include=`"AchieveAi.LmDotnetTools.OpenAiProvider`" Version=`"1.0.20`" />"
        $output += "  </ItemGroup>"
        continue
    }
    
    # Skip lines 69-84 (indices 68-83)
    if ($skip) {
        $skipCount++
        if ($skipCount -ge 16) {  # Skip 16 lines (69-84)
            $skip = $false
        }
        continue
    }
    
    $output += $line
}

$output | Set-Content AIChat.Server.csproj.new
Move-Item -Force AIChat.Server.csproj.new AIChat.Server.csproj
