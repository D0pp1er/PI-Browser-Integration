# Form Assistant

1. Double-click **Form Assistant.exe**. Pi starts by itself.
2. Enter **/browser-connect** in Pi to open or reconnect Assistant Browser (Edge), or double-click **Open Assistant Browser**. Then open the page you want. To test with the dummy form, press Ctrl+O in that Edge window and choose `form.html` from this folder.
3. In Pi, try **Read this page** or **Help me fill this form**. Give the requested details and review the confirmation.
4. When you are ready to submit, say **Submit this form** and approve the submit click. On the dummy page, look for the receipt below the form.

For Chrome, enable remote debugging at `chrome://inspect/#remote-debugging`, enter `/browser-connect chrome` in Pi, and approve the prompt if shown. If the page is in another tab, use `/browser-tab` to select it. The next Pi launch resumes the same conversation.

See `architecture.md` for the design and `CHANGELOG.md` for dated test results.
