# Planter

The **plant simulation** from [**Cloud Gardens**](https://store.steampowered.com/app/1372320/Cloud_Gardens/) 
as a Unity package for level design.

**Only Tested with Unity 2022 and up. It should work in older versions, because it's just C#8.0+ features that are missing.**

## Getting Started

1. Open the Package Manager in Unity.
2. Select **"Add package from git URL..."**
3. Paste `https://github.com/noio/games.noio.planter.git`
4. Open the "Samples" tab and **install the "Sample Plant Setup"**.
5. Open `Sample Plant Setup/Scene/Sample Scene`



![example.gif](Docs%7E%2Fexample.gif)

## How It Works

Plants are generated based on **Branches**. Each branch is a small piece of the plant,
which will spawn "child" branches in specific positions.

Branches are set up in **Branch Templates**, prefabs that determine
what the branch looks like and where the **Sockets** are for child branches.

Open up `Sample Plant Setup/Sample Branch` for an example:

![branch_template.png](Docs%7E%2Fbranch_template.png)

The **Branch Template** contains a bunch of configuration determining
how and where this branch will grow, this is the meat of the plant setup:

![branch_template_inspector.png](Docs%7E%2Fbranch_template_inspector.png)

For each **Branch Socket**, you can set **which types of branches** are allowed to grow there, 
with a percentage probability:

![branch_socket_options.png](Docs%7E%2Fbranch_socket_options.png)