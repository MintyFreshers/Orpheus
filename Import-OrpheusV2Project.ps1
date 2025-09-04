# Orpheus v2 Project Import Script
# This script creates a GitHub project and imports the comprehensive rebuild plan
# 
# Prerequisites:
# - GitHub CLI (gh) installed and authenticated
# - PowerShell 5.1 or higher
# - Repository access permissions

param(
    [Parameter(Mandatory=$true)]
    [string]$Owner,
    
    [Parameter(Mandatory=$true)]
    [string]$Repository,
    
    [Parameter(Mandatory=$false)]
    [string]$ProjectPlanPath = "orpheus-v2-project-plan.json"
)

# Color functions for better output
function Write-Success {
    param([string]$Message)
    Write-Host "✅ $Message" -ForegroundColor Green
}

function Write-Info {
    param([string]$Message)
    Write-Host "ℹ️  $Message" -ForegroundColor Cyan
}

function Write-Warning {
    param([string]$Message)
    Write-Host "⚠️  $Message" -ForegroundColor Yellow
}

function Write-Error {
    param([string]$Message)
    Write-Host "❌ $Message" -ForegroundColor Red
}

# Check prerequisites
Write-Info "Checking prerequisites..."

# Check if GitHub CLI is installed
try {
    $ghVersion = gh --version 2>$null
    if ($LASTEXITCODE -ne 0) {
        throw "GitHub CLI not found"
    }
    Write-Success "GitHub CLI found: $($ghVersion.Split("`n")[0])"
} catch {
    Write-Error "GitHub CLI (gh) is not installed or not in PATH. Please install it from https://cli.github.com"
    exit 1
}

# Check if authenticated
try {
    $authStatus = gh auth status 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Not authenticated"
    }
    Write-Success "GitHub CLI authenticated"
} catch {
    Write-Error "GitHub CLI is not authenticated. Run 'gh auth login' first."
    exit 1
}

# Check if project plan file exists
if (-not (Test-Path $ProjectPlanPath)) {
    Write-Error "Project plan file not found: $ProjectPlanPath"
    exit 1
}

Write-Success "Project plan file found: $ProjectPlanPath"

# Load project plan
try {
    $projectPlan = Get-Content $ProjectPlanPath -Raw | ConvertFrom-Json
    Write-Success "Project plan loaded successfully"
    Write-Info "Project: $($projectPlan.name)"
    Write-Info "Phases: $($projectPlan.phases.Count)"
    Write-Info "Total items: $(($projectPlan.phases | ForEach-Object { $_.items.Count } | Measure-Object -Sum).Sum)"
} catch {
    Write-Error "Failed to parse project plan JSON: $($_.Exception.Message)"
    exit 1
}

# Create GitHub Project
Write-Info "Creating GitHub project..."

try {
    $createProjectCmd = @(
        "gh", "project", "create",
        "--owner", $Owner,
        "--title", $projectPlan.name,
        "--body", $projectPlan.description
    )
    
    $projectOutput = & $createProjectCmd 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to create project: $projectOutput"
    }
    
    # Extract project URL and number from output
    $projectUrl = $projectOutput | Where-Object { $_ -like "*https://github.com/users/*/projects/*" -or $_ -like "*https://github.com/orgs/*/projects/*" }
    
    if ($projectUrl) {
        Write-Success "Project created: $projectUrl"
        # Extract project number from URL
        $projectNumber = ($projectUrl -split '/projects/')[-1]
        Write-Info "Project number: $projectNumber"
    } else {
        Write-Warning "Project created but URL not found in output"
        $projectNumber = $null
    }
} catch {
    Write-Error "Failed to create GitHub project: $($_.Exception.Message)"
    exit 1
}

# Create labels in repository
Write-Info "Creating labels in repository..."

foreach ($label in $projectPlan.labels) {
    try {
        $createLabelCmd = @(
            "gh", "label", "create",
            "--repo", "$Owner/$Repository",
            "--name", $label.name,
            "--color", $label.color,
            "--description", $label.description,
            "--force"  # Overwrite if exists
        )
        
        $labelOutput = & $createLabelCmd 2>&1
        if ($LASTEXITCODE -eq 0) {
            Write-Success "Created label: $($label.name)"
        } else {
            Write-Warning "Label may already exist: $($label.name)"
        }
    } catch {
        Write-Warning "Failed to create label '$($label.name)': $($_.Exception.Message)"
    }
}

# Create milestones in repository
Write-Info "Creating milestones in repository..."

foreach ($milestone in $projectPlan.milestones) {
    try {
        $createMilestoneCmd = @(
            "gh", "milestone", "create",
            "--repo", "$Owner/$Repository",
            "--title", $milestone.title,
            "--description", $milestone.description
        )
        
        $milestoneOutput = & $createMilestoneCmd 2>&1
        if ($LASTEXITCODE -eq 0) {
            Write-Success "Created milestone: $($milestone.title)"
        } else {
            Write-Warning "Failed to create milestone '$($milestone.title)': $milestoneOutput"
        }
    } catch {
        Write-Warning "Failed to create milestone '$($milestone.title)': $($_.Exception.Message)"
    }
}

# Create issues for each item
Write-Info "Creating issues from project plan..."

$totalIssues = ($projectPlan.phases | ForEach-Object { $_.items.Count } | Measure-Object -Sum).Sum
$currentIssue = 0

foreach ($phase in $projectPlan.phases) {
    Write-Info "Processing Phase $($phase.phase): $($phase.name)"
    
    foreach ($item in $phase.items) {
        $currentIssue++
        Write-Info "Creating issue $currentIssue/$totalIssues : $($item.title)"
        
        try {
            # Prepare labels parameter
            $labelsParam = $item.labels -join ","
            
            # Create issue
            $createIssueCmd = @(
                "gh", "issue", "create",
                "--repo", "$Owner/$Repository",
                "--title", $item.title,
                "--body", $item.body,
                "--label", $labelsParam
            )
            
            # Add milestone if specified
            if ($item.milestone) {
                $createIssueCmd += @("--milestone", $item.milestone)
            }
            
            $issueOutput = & $createIssueCmd 2>&1
            if ($LASTEXITCODE -eq 0) {
                $issueNumber = ($issueOutput -split '#')[-1] -split ' ' | Select-Object -First 1
                Write-Success "Created issue #$issueNumber : $($item.title)"
                
                # Add to project if project number is available
                if ($projectNumber) {
                    try {
                        $addToProjectCmd = @(
                            "gh", "project", "item-add",
                            "--owner", $Owner,
                            $projectNumber,
                            "--url", "https://github.com/$Owner/$Repository/issues/$issueNumber"
                        )
                        
                        & $addToProjectCmd 2>&1 | Out-Null
                        if ($LASTEXITCODE -eq 0) {
                            Write-Success "Added issue #$issueNumber to project"
                        }
                    } catch {
                        Write-Warning "Failed to add issue #$issueNumber to project: $($_.Exception.Message)"
                    }
                }
            } else {
                Write-Error "Failed to create issue: $issueOutput"
            }
        } catch {
            Write-Error "Failed to create issue '$($item.title)': $($_.Exception.Message)"
        }
        
        # Small delay to avoid rate limiting
        Start-Sleep -Milliseconds 500
    }
}

Write-Success "Project import completed!"
Write-Info "Summary:"
Write-Info "- Project created with kanban board"
Write-Info "- $($projectPlan.labels.Count) labels created"
Write-Info "- $($projectPlan.milestones.Count) milestones created"
Write-Info "- $totalIssues issues created across $($projectPlan.phases.Count) phases"

if ($projectUrl) {
    Write-Success "Visit your project: $projectUrl"
}

Write-Info "Next steps:"
Write-Info "1. Review the project board and organize items"
Write-Info "2. Set up project views and automation rules"
Write-Info "3. Start with Phase 1 - Foundation items"
Write-Info "4. Create a new repository for the v2 rebuild"
Write-Info "5. Begin implementation following the structured plan"

Write-Success "Happy coding! 🎵🤖"