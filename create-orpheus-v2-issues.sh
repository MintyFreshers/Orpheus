#!/bin/bash

# Simple bash script to create issues from the project plan
# Alternative to the PowerShell script for Linux/macOS users

# Configuration
OWNER="$1"
REPO="$2"
JSON_FILE="${3:-orpheus-v2-project-plan.json}"

if [ -z "$OWNER" ] || [ -z "$REPO" ]; then
    echo "Usage: $0 <owner> <repository> [json-file]"
    echo "Example: $0 MintyFreshers orpheus-v2"
    exit 1
fi

# Check if GitHub CLI is installed
if ! command -v gh &> /dev/null; then
    echo "❌ GitHub CLI is required. Install from https://cli.github.com"
    exit 1
fi

# Check if authenticated
if ! gh auth status &> /dev/null; then
    echo "❌ Please authenticate with GitHub CLI: gh auth login"
    exit 1
fi

# Check if JSON file exists
if [ ! -f "$JSON_FILE" ]; then
    echo "❌ Project plan file not found: $JSON_FILE"
    exit 1
fi

echo "✅ Creating Orpheus v2 project structure..."

# Extract project info from JSON
PROJECT_NAME=$(jq -r '.name' "$JSON_FILE")
PROJECT_DESC=$(jq -r '.description' "$JSON_FILE")

echo "📋 Project: $PROJECT_NAME"

# Create labels
echo "🏷️  Creating labels..."
jq -r '.labels[] | @json' "$JSON_FILE" | while read -r label; do
    name=$(echo "$label" | jq -r '.name')
    color=$(echo "$label" | jq -r '.color')
    description=$(echo "$label" | jq -r '.description')
    
    gh label create "$name" --color "$color" --description "$description" --repo "$OWNER/$REPO" --force 2>/dev/null || true
    echo "  Created: $name"
done

# Create milestones
echo "🎯 Creating milestones..."
jq -r '.milestones[] | @json' "$JSON_FILE" | while read -r milestone; do
    title=$(echo "$milestone" | jq -r '.title')
    description=$(echo "$milestone" | jq -r '.description')
    
    gh milestone create "$title" --description "$description" --repo "$OWNER/$REPO" 2>/dev/null || true
    echo "  Created: $title"
done

# Create issues
echo "📝 Creating issues..."
issue_count=0

jq -r '.phases[] | .items[] | @json' "$JSON_FILE" | while read -r item; do
    title=$(echo "$item" | jq -r '.title')
    body=$(echo "$item" | jq -r '.body')
    labels=$(echo "$item" | jq -r '.labels | join(",")')
    milestone=$(echo "$item" | jq -r '.milestone // empty')
    
    issue_count=$((issue_count + 1))
    
    # Create issue command
    cmd="gh issue create --repo \"$OWNER/$REPO\" --title \"$title\" --body \"$body\" --label \"$labels\""
    
    if [ -n "$milestone" ]; then
        cmd="$cmd --milestone \"$milestone\""
    fi
    
    eval "$cmd" >/dev/null 2>&1
    echo "  Created issue: $title"
    
    # Rate limiting
    sleep 0.5
done

echo ""
echo "✅ Project structure created successfully!"
echo "🔗 Visit: https://github.com/$OWNER/$REPO/issues"
echo ""
echo "Next steps:"
echo "1. Create a new GitHub Project and link these issues"
echo "2. Set up project views and automation"
echo "3. Start with Phase 1 - Foundation items"
echo ""
echo "🎵 Happy coding!"