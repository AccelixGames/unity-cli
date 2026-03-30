package client

import (
	"encoding/json"
	"os"
	"path/filepath"
	"testing"
)

func stubIsProcessRunning(t *testing.T, alivePIDs map[int]bool) {
	t.Helper()
	orig := isProcessRunning
	isProcessRunning = func(pid int) bool {
		return alivePIDs[pid]
	}
	t.Cleanup(func() { isProcessRunning = orig })
}

func writeInstanceFiles(t *testing.T, files map[string]Instance) string {
	t.Helper()
	home := t.TempDir()
	dir := filepath.Join(home, ".unity-cli", "instances")
	if err := os.MkdirAll(dir, 0755); err != nil {
		t.Fatalf("failed to create instances dir: %v", err)
	}
	for name, inst := range files {
		data, err := json.Marshal(inst)
		if err != nil {
			t.Fatalf("failed to marshal instance: %v", err)
		}
		if err := os.WriteFile(filepath.Join(dir, name), data, 0644); err != nil {
			t.Fatalf("failed to write instance file: %v", err)
		}
	}
	return home
}

// TestFindByPort_SkipsStoppedPicksLatest verifies the core bug fix:
// when a stopped instance and a ready instance share the same port,
// FindByPort must return the ready instance with the latest timestamp.
func TestFindByPort_SkipsStoppedPicksLatest(t *testing.T) {
	stubIsProcessRunning(t, map[int]bool{100: true, 200: true})

	home := writeInstanceFiles(t, map[string]Instance{
		// Alphabetically first — the old bug would pick this one
		"aaa_stopped.json": {
			State:       "stopped",
			ProjectPath: "/projects/old",
			Port:        8090,
			PID:         100,
			Timestamp:   1000,
		},
		"bbb_ready.json": {
			State:       "ready",
			ProjectPath: "/projects/current",
			Port:        8090,
			PID:         200,
			Timestamp:   2000,
		},
	})
	t.Setenv("HOME", home)

	got, err := FindByPort(8090)
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if got.State != "ready" {
		t.Errorf("State: got %q, want %q", got.State, "ready")
	}
	if got.ProjectPath != "/projects/current" {
		t.Errorf("ProjectPath: got %q, want %q", got.ProjectPath, "/projects/current")
	}
	if got.Timestamp != 2000 {
		t.Errorf("Timestamp: got %d, want %d", got.Timestamp, 2000)
	}
}

// TestFindByPort_PicksLatestTimestamp verifies that among multiple active
// instances on the same port, the one with the newest timestamp wins.
func TestFindByPort_PicksLatestTimestamp(t *testing.T) {
	stubIsProcessRunning(t, map[int]bool{100: true, 200: true})

	home := writeInstanceFiles(t, map[string]Instance{
		"aaa_old.json": {
			State:       "ready",
			ProjectPath: "/projects/old",
			Port:        8090,
			PID:         100,
			Timestamp:   1000,
		},
		"bbb_new.json": {
			State:       "ready",
			ProjectPath: "/projects/new",
			Port:        8090,
			PID:         200,
			Timestamp:   2000,
		},
	})
	t.Setenv("HOME", home)

	got, err := FindByPort(8090)
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if got.Timestamp != 2000 {
		t.Errorf("Timestamp: got %d, want %d", got.Timestamp, 2000)
	}
}

// TestScanInstances_RemovesDeadPID verifies that instance files with
// a non-running PID are removed from disk and excluded from results.
func TestScanInstances_RemovesDeadPID(t *testing.T) {
	stubIsProcessRunning(t, map[int]bool{
		100: false, // dead
		200: true,  // alive
	})

	home := writeInstanceFiles(t, map[string]Instance{
		"dead.json": {
			State:       "ready",
			ProjectPath: "/projects/dead",
			Port:        8090,
			PID:         100,
			Timestamp:   1000,
		},
		"alive.json": {
			State:       "ready",
			ProjectPath: "/projects/alive",
			Port:        8091,
			PID:         200,
			Timestamp:   2000,
		},
	})
	t.Setenv("HOME", home)

	instances, err := ScanInstances()
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}

	if len(instances) != 1 {
		t.Fatalf("expected 1 instance, got %d", len(instances))
	}
	if instances[0].ProjectPath != "/projects/alive" {
		t.Errorf("expected alive instance, got %q", instances[0].ProjectPath)
	}

	// Verify the dead file was actually deleted
	deadPath := filepath.Join(home, ".unity-cli", "instances", "dead.json")
	if _, err := os.Stat(deadPath); !os.IsNotExist(err) {
		t.Error("dead.json should have been deleted")
	}
}

// TestScanInstances_KeepsZeroPID verifies that instances with PID 0
// (e.g. legacy files) are kept without process checking.
func TestScanInstances_KeepsZeroPID(t *testing.T) {
	stubIsProcessRunning(t, map[int]bool{})

	home := writeInstanceFiles(t, map[string]Instance{
		"legacy.json": {
			State:       "ready",
			ProjectPath: "/projects/legacy",
			Port:        8090,
			PID:         0,
			Timestamp:   1000,
		},
	})
	t.Setenv("HOME", home)

	instances, err := ScanInstances()
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if len(instances) != 1 {
		t.Fatalf("expected 1 instance, got %d", len(instances))
	}
}
