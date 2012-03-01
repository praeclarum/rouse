
SHELL = bash

TESTS = $(shell find tests -name '*.js')

all: test

test:
	node server.js --test $(TESTS)

publish:
	npm publish .

