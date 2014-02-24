### Overview ###

AccuRev2Git is a tool to convert an AccuRev depot into a git repo. A specified AccuRev stream will be the target of the conversion, and all promotes to that stream will be turned into commits within the new git repository.

### Getting Started ###
- Update the users.txt file.

    The format is described within the file itself
- Update the app.config file.

    Make sure the paths to accurev.exe and git.exe are correct for your machine, and set the default git user name
3. Compile the solution

### How to use ###

#### Converting a Depot
The following will convert an AccuRev depot named "acdepot", using the AccuRev stream named "acstream" as the basis. The working directory for the git repo is the current directory. The converter will use "acuser"/"acpass" as credentials for logging into AccuRev. Resuming a conversion is supported via the "-r true" switch, so this command can be run multiple times and will automatically resume from where it left off.

```bat
accurev2git.exe -d acdepot -s acstream -w . -r true -u acuser -p acpass
```

---
---

Copyright (c) 2014 Ryan LaNeve

Permission is hereby granted, free of charge, to any person
obtaining a copy of this software and associated documentation
files (the "Software"), to deal in the Software without restriction,
including without limitation the rights to use, copy, modify, merge,
publish, distribute, sublicense, and/or sell copies of the Software,
and to permit persons to whom the Software is furnished to do so,
subject to the following conditions:

The above copyright notice and this permission notice shall be
included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.
IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY
CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT,
TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE
SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
