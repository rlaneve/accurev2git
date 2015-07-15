### Overview ###

AccuRev2Git is a tool to convert an AccuRev depot into a git repo. A specified AccuRev stream will be the target of the conversion, and all promotes to that stream will be turned into commits within the new git repository.

### NOTICE ###
Lazar Sumar has an alternate implementation available [here](https://github.com/orao/ac2git) which takes advantage of functionality found in later AccuRev clients than from when I wrote this converter. It is being worked on and improved, whereas mine is not. Mine served its purpose, and I have no intention of working on it again.

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

[License](LICENSE.md)
