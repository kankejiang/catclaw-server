package dht

import (
	"fmt"
	"math/big"
	"net"
	"sync"
)

// NodeID is a 160-bit identifier (SHA-1 hash space).
type NodeID [20]byte

// Xor returns the XOR distance between two NodeIDs as a big.Int.
func (id NodeID) Xor(other NodeID) *big.Int {
	x := make([]byte, 20)
	for i := 0; i < 20; i++ {
		x[i] = id[i] ^ other[i]
	}
	return new(big.Int).SetBytes(x)
}

// Less returns true if id < other in big.Int terms.
func (id NodeID) Less(other NodeID) bool {
	for i := 0; i < 20; i++ {
		if id[i] < other[i] {
			return true
		}
		if id[i] > other[i] {
			return false
		}
	}
	return false
}

// String returns a hex string representation.
func (id NodeID) String() string {
	return fmt.Sprintf("%x", id[:])
}

// PrefixLen returns the number of matching prefix bits.
func (id NodeID) PrefixLen(other NodeID) int {
	for i := 0; i < 20; i++ {
		if id[i] != other[i] {
			return i*8 + prefixLenByte(id[i]^other[i])
		}
	}
	return 160
}

func prefixLenByte(b byte) int {
	for i := 7; i >= 0; i-- {
		if b&(1<<i) != 0 {
			return 7 - i
		}
	}
	return 8
}

// Contact represents a remote DHT node.
type Contact struct {
	ID         NodeID `json:"id"`
	IP         net.IP `json:"ip"`
	Port       int    `json:"port"`
	DeviceName string `json:"device_name"`
}

// Message types for DHT RPC.
const (
	MsgPing       = "ping"
	MsgPong       = "pong"
	MsgFindNode   = "find_node"
	MsgFindNodeR  = "find_node_r"
	MsgStore      = "store"
	MsgStoreR     = "store_r"
	MsgFindValue  = "find_value"
	MsgFindValueR = "find_value_r"
)

// Message represents a DHT RPC message.
type Message struct {
	Type     string    `json:"t"`
	Sender   Contact   `json:"s"`
	TargetID NodeID    `json:"tid,omitempty"`
	Key      string    `json:"key,omitempty"`
	Value    string    `json:"val,omitempty"`
	Contacts []Contact `json:"contacts,omitempty"`
}

// StoredValue holds a value stored in the DHT.
type StoredValue struct {
	Key       string
	Value     string
	ExpiresAt int64
}

// RoutingTable implements Kademlia k-buckets.
type RoutingTable struct {
	self    NodeID
	buckets [160][]Contact
	k       int
	mu      sync.RWMutex
}

// NewRoutingTable creates a new routing table.
func NewRoutingTable(self NodeID, k int) *RoutingTable {
	rt := &RoutingTable{
		self: self,
		k:    k,
	}
	for i := range rt.buckets {
		rt.buckets[i] = make([]Contact, 0, k)
	}
	return rt
}

// Add adds or updates a contact.
func (rt *RoutingTable) Add(c Contact) {
	if c.ID == rt.self {
		return
	}
	rt.mu.Lock()
	defer rt.mu.Unlock()

	plen := c.ID.PrefixLen(rt.self)
	if plen >= 160 {
		return
	}
	bucket := rt.buckets[plen]

	for i, existing := range bucket {
		if existing.ID == c.ID {
			copy(bucket[1:i+1], bucket[:i])
			bucket[0] = c
			rt.buckets[plen] = bucket
			return
		}
	}

	if len(bucket) < rt.k {
		rt.buckets[plen] = append([]Contact{c}, bucket...)
	} else {
		copy(bucket[1:], bucket[:rt.k-1])
		bucket[0] = c
	}
}

// FindClosest returns the k closest contacts to target.
func (rt *RoutingTable) FindClosest(target NodeID, k int) []Contact {
	rt.mu.RLock()
	defer rt.mu.RUnlock()

	type pair struct {
		c    Contact
		dist *big.Int
	}
	var candidates []pair
	for _, bucket := range rt.buckets {
		for _, c := range bucket {
			candidates = append(candidates, pair{c, target.Xor(c.ID)})
		}
	}

	// Sort by distance
	for i := 0; i < len(candidates); i++ {
		for j := i + 1; j < len(candidates); j++ {
			if candidates[i].dist.Cmp(candidates[j].dist) > 0 {
				candidates[i], candidates[j] = candidates[j], candidates[i]
			}
		}
	}
	if len(candidates) > k {
		candidates = candidates[:k]
	}
	result := make([]Contact, len(candidates))
	for i, p := range candidates {
		result[i] = p.c
	}
	return result
}

// AllContacts returns all contacts in the routing table.
func (rt *RoutingTable) AllContacts() []Contact {
	rt.mu.RLock()
	defer rt.mu.RUnlock()
	var all []Contact
	for _, bucket := range rt.buckets {
		all = append(all, bucket...)
	}
	return all
}

// Size returns the total number of contacts.
func (rt *RoutingTable) Size() int {
	rt.mu.RLock()
	defer rt.mu.RUnlock()
	count := 0
	for _, bucket := range rt.buckets {
		count += len(bucket)
	}
	return count
}
