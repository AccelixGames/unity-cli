//go:build !windows

package client

import (
	"os"
	"syscall"
)

func checkProcessRunning(pid int) bool {
	proc, err := os.FindProcess(pid)
	if err != nil {
		return false
	}
	return proc.Signal(syscall.Signal(0)) == nil
}
