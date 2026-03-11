package cmd

import (
	"strconv"

	"github.com/youngwoocho02/unity-cli/internal/client"
)

func consoleCmd(args []string, send sendFn) (*client.CommandResponse, error) {
	params := map[string]interface{}{}

	flags := parseSubFlags(args)

	if v, ok := flags["lines"]; ok {
		if n, err := strconv.Atoi(v); err == nil {
			params["maxLines"] = n
		}
	}
	if v, ok := flags["filter"]; ok {
		params["filter"] = v
	}

	return send("read_console", params)
}
