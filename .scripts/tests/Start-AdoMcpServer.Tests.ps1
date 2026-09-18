#Requires -Version 7.4
#Requires -Modules @{ ModuleName = 'Pester'; RequiredVersion = '5.7.1' }

BeforeAll {
    . (Join-Path $PSScriptRoot '..' 'Start-AdoMcpServer.ps1') -Organisation 'synthetic-org'
}

Describe 'Start-AdoMcpServer.ps1' -Tag 'Unit' {
    Context 'dotenv parsing' {
        It 'Reads the last exact key and preserves equals signs inside a quoted value' {
            $envPath = Join-Path $TestDrive '.env'
            @(
                '# synthetic fixture'
                'OTHER_VALUE=ignored'
                'AZURE_DEVOPS_PAT=first-value'
                'export AZURE_DEVOPS_PAT = "file=value=="'
            ) | Set-Content -LiteralPath $envPath

            Get-DotEnvValue -Path $envPath -Name 'AZURE_DEVOPS_PAT' | Should -BeExactly 'file=value=='
        }

        It 'Returns null when the exact key is absent' {
            $envPath = Join-Path $TestDrive '.env'
            'AZURE_DEVOPS_PAT_SUFFIX=not-the-token' | Set-Content -LiteralPath $envPath

            Get-DotEnvValue -Path $envPath -Name 'AZURE_DEVOPS_PAT' | Should -BeNullOrEmpty
        }
    }

    Context 'credential precedence' {
        It 'Prefers the process environment token over the dotenv token' {
            $envPath = Join-Path $TestDrive '.env'
            'AZURE_DEVOPS_PAT=file-token' | Set-Content -LiteralPath $envPath

            Resolve-AdoPat -EnvironmentToken 'environment-token' -Path $envPath |
                Should -BeExactly 'environment-token'
        }

        It 'Uses the dotenv token when the process environment token is empty' {
            $envPath = Join-Path $TestDrive '.env'
            "AZURE_DEVOPS_PAT='file-token'" | Set-Content -LiteralPath $envPath

            Resolve-AdoPat -EnvironmentToken '' -Path $envPath | Should -BeExactly 'file-token'
        }

        It 'Rejects a missing credential' {
            {
                Resolve-AdoPat -EnvironmentToken '' -Path (Join-Path $TestDrive 'missing.env')
            } | Should -Throw '*No credential found*'
        }
    }

    Context 'credential encoding' {
        It 'Base64 encodes the required username and raw PAT shape' {
            $encoded = ConvertTo-AdoMcpCredential -Token 'synthetic-token'
            [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($encoded)) |
                Should -BeExactly 'yamlizr:synthetic-token'
        }
    }

    Context 'npx invocation' {
        BeforeEach {
            $script:originalPersonalAccessToken = $env:PERSONAL_ACCESS_TOKEN
            Mock Invoke-NpxCommand { $script:NpxExitCode = 0 }
        }

        AfterEach {
            $env:PERSONAL_ACCESS_TOKEN = $script:originalPersonalAccessToken
        }

        It 'Passes the organisation, PAT authentication, and domains as discrete arguments' {
            $output = Start-AdoMcpServer `
                -OrganisationName 'synthetic-org' `
                -RawToken 'synthetic-token' `
                -NpxPath 'npx-shim' `
                -Domains @('core', 'repositories')

            $output | Should -BeNullOrEmpty
            Should -Invoke Invoke-NpxCommand -Times 1 -Exactly -ParameterFilter {
                $Path -eq 'npx-shim' -and
                ($ArgumentList -join '|') -eq '-y|@azure-devops/mcp|synthetic-org|--authentication|pat|-d|core|repositories'
            }
        }

        It 'Exports only the derived MCP credential to the child process environment' {
            Start-AdoMcpServer `
                -OrganisationName 'synthetic-org' `
                -RawToken 'synthetic-token' `
                -NpxPath 'npx-shim'

            $decoded = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($env:PERSONAL_ACCESS_TOKEN))
            $decoded | Should -BeExactly 'yamlizr:synthetic-token'
        }
    }

    Context 'stdout safety' {
        It 'Writes diagnostics to stderr and leaves stdout empty' {
            $stdout = [IO.StringWriter]::new()
            $stderr = [IO.StringWriter]::new()
            $originalOut = [Console]::Out
            $originalError = [Console]::Error

            try {
                [Console]::SetOut($stdout)
                [Console]::SetError($stderr)
                Write-Diagnostic 'synthetic diagnostic'
            }
            finally {
                [Console]::SetOut($originalOut)
                [Console]::SetError($originalError)
            }

            $stdout.ToString() | Should -BeNullOrEmpty
            $stderr.ToString() | Should -Match 'synthetic diagnostic'
        }
    }
}