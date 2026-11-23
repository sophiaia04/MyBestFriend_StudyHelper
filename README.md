# StudyHelper

StudyHelper is a desktop study assistant built with **C#**, **.NET 8**, and **Avalonia UI**. Upload study material, then summarize it, request clear explanations, or generate and complete practice quizzes with Google Gemini.

## Features

- Upload PDF, DOCX, TXT, and Markdown files
- Generate summaries from uploaded documents
- Ask for detailed, beginner-friendly explanations
- Create configurable multiple-choice quizzes
- Check quiz answers with scores and explanations
- Use independent chat sessions for summary, explanation, and quiz tasks
- Browse previous questions in a combined history
- Keep chat and API key settings between sessions
- Reply in the language used by the student

## Requirements

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or newer
- A [Google Gemini API key](https://ai.google.dev/)

## Getting Started

Clone the repository and start the app:

```bash
git clone https://github.com/sophiaia04/MyBestFriend_StudyHelper.git
cd MyBestFriend_StudyHelper
dotnet restore
dotnet run
```

When the app opens:

1. Open **Gemini API Key**.
2. Paste your API key and select **Save**.
3. Select **Upload Files** and add one or more study documents.
4. Use **Summarize**, **Explain**, or **Make Quiz**.
5. Ask follow-up questions in the corresponding chat tab.

You can also provide the API key through an environment variable instead of saving it in the app:

```bash
export GEMINI_API_KEY="your-api-key"
dotnet run
```

On Windows PowerShell:

```powershell
$env:GEMINI_API_KEY="your-api-key"
dotnet run
```

## Build

Build a debug executable:

```bash
dotnet build
```

Publish a release build:

```bash
dotnet publish -c Release
```

The published app is written to `bin/Release/net8.0/publish/`.

## Data Storage and Privacy

StudyHelper stores its settings and chat history in the operating system's application-data directory:

- **Windows:** `%APPDATA%\StudyHelper`
- **macOS:** `~/Library/Application Support/StudyHelper`
- **Linux:** `~/.config/StudyHelper`

The API key is read from `GEMINI_API_KEY` first, if set, and otherwise from the app settings. Uploaded document content is sent to Gemini as part of the prompt when requesting a response.

## Technology

- Avalonia UI
- CommunityToolkit.Mvvm
- Google Gen AI SDK
- PdfPig for PDF text extraction
- DOCX text extraction from `word/document.xml`