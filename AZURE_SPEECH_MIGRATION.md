# Azure Speech Service Migration

This document outlines the migration from Whisper to Azure Cognitive Services Speech-to-Text for improved transcription performance.

## Changes Made

### 1. Package Dependencies
- **Removed**: `Whisper.net` (v1.8.1) and `Whisper.net.Runtime` (v1.8.1)
- **Added**: `Microsoft.CognitiveServices.Speech` (v1.40.0)

### 2. New Service Implementation
- **Created**: `AzureSpeechTranscriptionService.cs`
  - Implements the existing `ITranscriptionService` interface
  - Provides real-time speech recognition with Azure Speech Service
  - Optimized for low-latency voice command recognition
  - Handles audio format conversion from Discord (48kHz) to Azure (16kHz)

### 3. Configuration Updates
- **Updated**: `appsettings.example.json` to include Azure Speech configuration:
  ```json
  "AzureSpeech": {
    "SubscriptionKey": "<YOUR_AZURE_SPEECH_SUBSCRIPTION_KEY_HERE>",
    "Region": "eastus"
  }
  ```

### 4. Service Registration
- **Updated**: `Program.cs` to register `AzureSpeechTranscriptionService` instead of `WhisperTranscriptionService`

### 5. File Removal
- **Deleted**: `WhisperTranscriptionService.cs` (no longer needed)

### 6. Test Updates
- **Updated**: `DependencyInjectionTests.cs` to reference the new service
- **Added**: `AzureSpeechTranscriptionServiceTests.cs` with comprehensive unit tests

### 7. Documentation Updates
- **Updated**: `VOICE_COMMANDS.md` to reflect Azure Speech Service usage
- Updated all references from "Whisper" to "Azure Speech Service"
- Added performance improvement notes

## Benefits of Migration

### Performance Improvements
1. **Faster Response Times**: Cloud-based processing eliminates model loading delays
2. **Better Accuracy**: Azure's state-of-the-art speech recognition models
3. **Lower Latency**: Optimized for real-time voice command recognition
4. **No Model Downloads**: Eliminates the 40MB Whisper model download requirement

### Technical Advantages
1. **Cloud Scalability**: Leverages Microsoft's cloud infrastructure
2. **Built-in Optimization**: Voice command-specific configuration options
3. **Better Error Handling**: Robust cloud service error management
4. **Format Flexibility**: Native support for various audio formats

## Configuration Requirements

### Azure Speech Service Setup
1. Create an Azure Cognitive Services Speech resource
2. Obtain the subscription key and region
3. Set configuration values:
   - `AzureSpeech:SubscriptionKey` in appsettings.json
   - `AzureSpeech:Region` in appsettings.json
   - Or use environment variables: `AZURE_SPEECH_KEY` and `AZURE_SPEECH_REGION`

### Environment Variables
The service supports configuration via environment variables for containerized deployments:
- `AZURE_SPEECH_KEY`: Azure Speech subscription key
- `AZURE_SPEECH_REGION`: Azure region (defaults to "eastus")

## Migration Impact

### Interface Compatibility
- ✅ **No breaking changes**: Existing `ITranscriptionService` interface maintained
- ✅ **Drop-in replacement**: No changes required to consuming services
- ✅ **Same API**: `TranscribeAudioAsync()`, `InitializeAsync()`, etc.

### Performance Characteristics
- ⚡ **Response Time**: Significantly improved (no model loading)
- 🎯 **Accuracy**: Better voice command recognition
- 📦 **Memory Usage**: Reduced (no local model storage)
- 🌐 **Network Usage**: Increased (cloud-based processing)

## Rollback Plan
If needed, rollback is straightforward:
1. Restore `Whisper.net` packages in `Orpheus.csproj`
2. Restore `WhisperTranscriptionService.cs`
3. Update service registration in `Program.cs`
4. Revert configuration changes

## Testing
- ✅ Unit tests pass for new Azure Speech service
- ✅ Dependency injection tests updated and passing
- ✅ Build succeeds without warnings
- ⏸️ **Manual testing required**: Real Azure Speech API testing requires valid subscription

## Next Steps
1. **Manual Testing**: Test with valid Azure Speech Service credentials
2. **Performance Validation**: Measure actual response time improvements
3. **Error Handling**: Validate error scenarios (network issues, quota limits)
4. **Documentation**: Update deployment guides with Azure Speech requirements