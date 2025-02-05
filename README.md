![WideBanner3](https://github.com/bawkee/MdcAi/assets/38323343/76a5b2f2-5afb-4810-b9f2-f341f59f7acd)

# MDC Ai

![WinUI3 Unpackaged](https://github.com/bawkee/mdcai/actions/workflows/dotnet-desktop.yml/badge.svg?event=push)

Native Windows desktop GPT agent app, which is your portal to the very powerful OpenAI API. This is a BYOK app (bring your own key). Privacy of your conversations is guaranteed, unlike with web agents. No intermediary services are involved, it's just you and the stateless API.

⚠ NOTE: App is currently unavailable in Microsoft Store, it will be back soon.

<a href="https://apps.microsoft.com/detail/MDC%20AI/9NW24N9W33C9?launch=true&mode=mini">
	<img src="https://get.microsoft.com/images/en-us%20dark.svg" width="200"/>
</a>

Click on the above Windows Store badge to install MDC AI. There is no direct/unofficial download yet.

![DarkWhiteModes](https://github.com/bawkee/MdcAi/assets/38323343/7c525d68-9910-4d74-a6f6-dbc3395df8e3)

## Planned Features
- Vector database of all the conversations so that:
	- Semantic search can be done to easily find past chats.
 	- New conversations can be augmented with past ones, if the user chooses.
- Multimodality with image-to-text and text-to-image capabilities (DALL-E).
- Custom tools, aka function calling, in a nutshell a possibility to describe a Python class or Powershell script that the LLM can run during a conversation. For example, define a Powershell script that deactivates an active directory user, so if that LLM determines that you wish to deactivate the user, it will ask you for required parameters (i.e. username) and execute the script, outputting results.
- An automated retrieval-augmented generation pipeline to be implemented using local storage. This includes a vector knowledge database of locally stored documents (such as PDFs and Word files) in a specified folder, enabling users to easily ask questions about their documents. Being a desktop app gives it a huge benefit of not having to upload large documents or pay for remote vector storage, you could theoretically put an entire library in it.
- Multimodality to be extended to use self-hosted Flux or Stable Diffusion for image-to-image generation, as well as inpainting and outpainting of existing images. Being a desktop app, it would have the potential to auto-install the entire desired AI pipeline.
- Possibility to Edit (correct) a bad completion directly instead of stacking multiple attempts of achieving the correct solution (thus stacking up context length and reducing prediction precision). Best example is with coding solutions where you hint to an incorrect code and instead of correcting the previous answer, it stacks up a new, possibly also incorrect answer, requiring another review. etc. which can add up and affect the performance and precision of future answers.
- Implementing other API endpoints such as Claude, DeepSeek and Mistral.
- Ability to auto-install and use self-hosted LLMs, similar to other chat UIs.

## Current Features
- Custom personalities (assistants), each with different parameters, models and system prompts.
- Integrated full-width Markdown renderer via WebView2. Unlike other desktop apps, where output is simple and unformatted (or barely formatted) text, this app formats source code, headings, paragraphs, bullet lists, etc. The output is fully selectable and snappy.
- Advanced Edit functionality, the Edit button allows you to create infinite number of nested forks within a conversation, branching out in different directions. Unlike in other apps and web UIs, nested forks are supported, and the app remembers your current fork, allowing you to continue where you left off. Why forking? Because it can save massive amount of credits, reducing token usage by up to 95% - few people know, but every time you stack another message to previous ones, even when you say "forget past messages", the LLM, being stateless, will still re-evaluate the entire stack of messages and charge all the tokens regardless.
- Full chat history that loads up instantly so you can scroll through hundreds of past chats and load them in an instant.
- Privacy. Unlike with Chat GPT subscriptions, your prompts are not saved or stored anywhere other than your computer. With subscription-based agents, your prompts are used for training and stored on an external database, which is vulnerable to hacking. The API used by external agents is statless.
- Costs and rate limits. OpenAI API pay-as-you-go approach, combined with the Edit functionality, can save you a lot of money in the long run. You are also free from standard rate limits when you need the LLM the most.

## Why Yet Another GPT Wrapper?

This is the only native, Windows desktop GPT App that:

- Has a clear privacy policy, is free, safe, secure and does not simply serve as a lead to some paid product or monetizes in some other, sneaky fashion. Your API key is safe with this app, there are no ulterior motives.
- Has a sleek UI that resembles Chat GPT, where markdown Just Works™, where you can select content and copy it with no problems and can search content via `Ctrl+F`. It is more compact than Chat GPT and displays conversations in compact, full-width mode which is great for code listings, tables and long documents.  
- Offers Edit functionality just like Chat GPT, with added feature where it remembers where you left off with your edits, unlike Chat GPT which restarts your message version selection. Edit is pivotal if you want to save $ on tokens or just keep your sanity in long conversations, don't tell the AI to "take a step back" or such, just edit the message and fork the conversation.
- Uses actual Windows native UI technology which respects your Windows theme settings, dark/light modes, accent colors and looks consistent with other Windows apps. It automatically adapts UI to your device (tablet/desktop), DPI settings, multi-monitor setups, battery performance settings, etc. by letting Windows manage it.
- Allows you to change AI settings and the "Premise" setting (GPT system message) in the middle of a conversation or across categories. This is extremely useful. If you have a PDF or a Word document you want to "discuss" on, just create a Category with that document as the premise and ask away. This is how all those "pdf chat" apps work anyway. GPT4 Turbo has a massive token limit so you won't need embeddings in 99% of use cases.

## Is it free?

Yes, it is completely free and open source. While it asks nothing of you, you can help by: 
- Rating it on the Microsoft store
- Adding a star on this GitHub repo
- Reporting issues and bugs
- Suggesting new features and ideas
- Contributing to the code base if you have dev experience
- Spreading the word and promoting the app further

## Version 1.0.2 is out 👇

- Fixed a bug where only the first two messages get saved with new conversations.
- Fixed a bug where selecting message versions without doing anything else would not remember the current version.
- Settings pane is now involved in the Back button (top left) logic.
- Fixed a bug where app could hang when long conversations are selected first after starting the app.
- Deleted categories will no longer magically reappear after restart.
- Fixed a few odd markdown rendering errors that would otherwise require app restart.
- When completion generation error happens, a friendlier message will show up and user will be able to retry.

## Screenshots

![CodingAssistant](https://github.com/bawkee/MdcAi/assets/38323343/86b40491-5075-49ba-853b-7654a7c61b1f)

![EdgeRendering](https://github.com/bawkee/MdcAi/assets/38323343/1429983d-a859-436d-b354-cb681c08dae0)

![OverridableAISettings](https://github.com/bawkee/MdcAi/assets/38323343/f7d2de43-a0d9-4c1f-978e-10f08a6d6abd)

