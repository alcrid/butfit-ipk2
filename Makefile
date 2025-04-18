.PHONY: all clean build publish

PROJECT_DIR=project2
PROJECT_FILE=$(PROJECT_DIR)/project2.csproj
OUTPUT=ipk25chat-client
RUNTIME=linux-x64
OUTPUT_DIR=$(PROJECT_DIR)/bin/Release

all: clean build publish

clean:
	dotnet clean $(PROJECT_DIR)

build:
	dotnet build -c Release

publish:
	dotnet publish $(PROJECT_FILE) -c Release -r $(RUNTIME) --self-contained true \
		-p:PublishSingleFile=true -p:PublishTrimmed=true -p:AssemblyName=$(OUTPUT) \
		-o $(OUTPUT_DIR)
	cp $(OUTPUT_DIR)/$(OUTPUT) ./$(OUTPUT)
