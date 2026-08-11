# Linqux

A simple Linq query tool that lets you query databases with Linq.

## Background

Born out of frustration that [Linqpad](https://www.linqpad.net) only works on Windows. And I'm a Linux user. This is not a full Linqpad replacement, instead it does 1% of what Linqpad does but it's the one percent that I care about.

## Caveats

This is at the moment only designed and tested for Azure SQL database since that's my use case. Maybe I'll implement more databases further on, but for now it solved my problem.
The models are scaffolded und `.config/Linqux/Models`, these need to be cleaned out manually

## Bugs

Now you need to re-scaffold the models for every session, this will be fixed. Sometime...

## Installation

`dotnet publish Linqux.UI/Linqux.UI.csproj -c Release -r linux-x64 --self-contained` and replace with whatever environment you have
