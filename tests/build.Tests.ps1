#Requires -Version 7.4
#Requires -Modules @{ ModuleName = 'Pester'; RequiredVersion = '5.7.1' }

BeforeAll {
    . (Join-Path $PSScriptRoot '..' 'build.ps1')
}

Describe 'build.ps1 resolution' -Tag 'Unit' {
    Context 'configuration defaults' {
        It 'Defaults to a Release build' {
            $Configuration | Should -BeExactly 'Release'
        }
    }

    Context 'compiled version resolution' {
        It 'Uses an explicit compiled version without consulting GitVersion' {
            $result = Resolve-Version -RequestedVersion '1.2.3' -Required $false -GitVersionResolver {
                throw 'GitVersion must not be called'
            }

            $result | Should -BeExactly '1.2.3'
        }

        It 'Trims the version returned by GitVersion' {
            Resolve-Version -RequestedVersion '' -Required $false -GitVersionResolver { ' 2.3.4 ' } |
            Should -BeExactly '2.3.4'
        }

        It 'Falls back for a local build when GitVersion is unavailable' {
            Resolve-Version -RequestedVersion '' -Required $false -GitVersionResolver { $null } -WarningAction SilentlyContinue |
            Should -BeExactly '0.0.1'
        }

        It 'Rejects a push build when no compiled version can be resolved' {
            {
                Resolve-Version -RequestedVersion '' -Required $true -GitVersionResolver { $null }
            } | Should -Throw '*push build requires*'
        }
    }

    Context 'platform resolution' {
        It 'Preserves an explicit platform list' {
            Resolve-Platforms -RequestedPlatforms 'linux/amd64,linux/arm64' -IsPush $false -Architecture 'Arm64' |
            Should -BeExactly 'linux/amd64,linux/arm64'
        }

        It 'Uses every published platform for a push build' {
            Resolve-Platforms -RequestedPlatforms '' -IsPush $true -Architecture 'X64' |
            Should -BeExactly 'linux/amd64,linux/arm64,linux/arm/v7'
        }

        It 'Maps host architecture <Architecture> to <Expected>' -ForEach @(
            @{ Architecture = 'X64'; Expected = 'linux/amd64' }
            @{ Architecture = 'Arm64'; Expected = 'linux/arm64' }
            @{ Architecture = 'Arm'; Expected = 'linux/arm/v7' }
        ) {
            Resolve-Platforms -RequestedPlatforms '' -IsPush $false -Architecture $Architecture |
            Should -BeExactly $Expected
        }
    }
}
