package dht

import (
	"context"
	"crypto/sha1"
	"encoding/json"
	"fmt"
	"log"
	"math/rand"
	"net"
	"strings"
	"sync"
	"time"
)

// Config holds DHT node configuration.
type Config struct {
	Port           int
	BootstrapNodes []string
	DeviceName     string
}

// Node is a Kademlia DHT node.
type Node struct {
	id           NodeID
	contact      Contact
	routingTable *RoutingTable
	store        map[string]StoredValue
	storeMu      sync.RWMutex
	conn         *net.UDPConn
	bootstrap    []string
	deviceName   string
	cancel       context.CancelFunc
	wg           sync.WaitGroup
}

// NewNode creates a new DHT node.
func NewNode(cfg Config) (*Node, error) {
	id := generateNodeID()
	ip := getOutboundIP()

	return &Node{
		id:      id,
		contact: Contact{ID: id, IP: ip, Port: cfg.Port, DeviceName: cfg.DeviceName},
		routingTable: NewRoutingTable(id, 20),
		store:      make(map[string]StoredValue),
		bootstrap:  cfg.BootstrapNodes,
		deviceName: cfg.DeviceName,
	}, nil
}

// Start begins the DHT node operations.
func (n *Node) Start(ctx context.Context) error {
	addr := &net.UDPAddr{Port: n.contact.Port}
	conn, err := net.ListenUDP("udp", addr)
	if err != nil {
		return fmt.Errorf("dht listen: %w", err)
	}
	n.conn = conn

	ctx, n.cancel = context.WithCancel(ctx)

	n.wg.Add(2)
	go n.readLoop(ctx)
	go n.maintenanceLoop(ctx)

	// Bootstrap: connect to known nodes
	go n.bootstrapJoin(ctx)

	return nil
}

// Stop gracefully stops the DHT node.
func (n *Node) Stop() {
	if n.cancel != nil {
		n.cancel()
	}
	if n.conn != nil {
		n.conn.Close()
	}
	n.wg.Wait()
}

// Contact returns this node's contact info.
func (n *Node) Contact() Contact {
	return n.contact
}

// RoutingTableSize returns the number of known peers.
func (n *Node) RoutingTableSize() int {
	return n.routingTable.Size()
}

// AllContacts returns all known contacts.
func (n *Node) AllContacts() []Contact {
	return n.routingTable.AllContacts()
}

// Store puts a key-value pair into the DHT.
func (n *Node) Store(key, value string) {
	n.storeMu.Lock()
	n.store[key] = StoredValue{
		Key:       key,
		Value:     value,
		ExpiresAt: time.Now().Add(24 * time.Hour).Unix(),
	}
	n.storeMu.Unlock()

	// Also replicate to k closest nodes
	targetID := sha1Hash([]byte(key))
	closest := n.routingTable.FindClosest(targetID, 20)
	for _, c := range closest {
		msg := Message{
			Type:   MsgStore,
			Sender: n.contact,
			Key:    key,
			Value:  value,
		}
		n.sendMessage(c, msg)
	}
}

// FindValue looks up a key in the DHT.
func (n *Node) FindValue(key string) []string {
	// Check local store first
	n.storeMu.RLock()
	if sv, ok := n.store[key]; ok && sv.ExpiresAt > time.Now().Unix() {
		n.storeMu.RUnlock()
		return []string{sv.Value}
	}
	n.storeMu.RUnlock()

	// Query network
	targetID := sha1Hash([]byte(key))
	closest := n.routingTable.FindClosest(targetID, 20)

	var results []string
	var resultsMu sync.Mutex
	var wg sync.WaitGroup

	for _, c := range closest {
		wg.Add(1)
		go func(c Contact) {
			defer wg.Done()
			msg := Message{
				Type:   MsgFindValue,
				Sender: n.contact,
				Key:    key,
			}
			resp, err := n.sendAndWait(c, msg, 5*time.Second)
			if err != nil {
				return
			}
			if resp.Type == MsgFindValueR && resp.Value != "" {
				resultsMu.Lock()
				results = append(results, resp.Value)
				resultsMu.Unlock()
			}
		}(c)
	}
	wg.Wait()
	return results
}

// StoreDevice registers this device's API endpoint in the DHT.
func (n *Node) StoreDevice(httpPort int) {
	value := fmt.Sprintf(`{"name":"%s","ip":"%s","http_port":%d,"dht_port":%d}`,
		n.deviceName, n.contact.IP.String(), httpPort, n.contact.Port)
	n.Store("device:"+n.id.String(), value)
}

// FindDevices searches for all music devices in the DHT.
func (n *Node) FindDevices() []string {
	return n.FindValue("device:")
}

func (n *Node) readLoop(ctx context.Context) {
	defer n.wg.Done()
	buf := make([]byte, 8192)
	for {
		select {
		case <-ctx.Done():
			return
		default:
		}
		n.conn.SetReadDeadline(time.Now().Add(2 * time.Second))
		nbytes, addr, err := n.conn.ReadFromUDP(buf)
		if err != nil {
			if netErr, ok := err.(net.Error); ok && netErr.Timeout() {
				continue
			}
			if strings.Contains(err.Error(), "closed") {
				return
			}
			log.Printf("[dht] read error: %v", err)
			continue
		}
		go n.handleMessage(buf[:nbytes], addr)
	}
}

func (n *Node) maintenanceLoop(ctx context.Context) {
	defer n.wg.Done()
	ticker := time.NewTicker(60 * time.Second)
	defer ticker.Stop()

	for {
		select {
		case <-ctx.Done():
			return
		case <-ticker.C:
			// Ping random nodes to keep routing table fresh
			contacts := n.routingTable.AllContacts()
			if len(contacts) > 0 {
				for _, c := range contacts {
					if rand.Intn(3) == 0 { // Ping ~1/3 of nodes
						msg := Message{Type: MsgPing, Sender: n.contact}
						n.sendMessage(c, msg)
					}
				}
			}

			// Clean expired stored values
			n.storeMu.Lock()
			now := time.Now().Unix()
			for k, v := range n.store {
				if v.ExpiresAt < now {
					delete(n.store, k)
				}
			}
			n.storeMu.Unlock()

			// Refresh self-bootstrap
			for _, addr := range n.bootstrap {
				resolvedIP, port, err := resolveAddr(addr)
				if err != nil {
					continue
				}
				msg := Message{Type: MsgPing, Sender: n.contact}
				n.sendMessage(Contact{IP: resolvedIP, Port: port}, msg)
			}
		}
	}
}

func (n *Node) bootstrapJoin(ctx context.Context) {
	for _, addr := range n.bootstrap {
		ip, port, err := resolveAddr(addr)
		if err != nil {
			log.Printf("[dht] Failed to resolve bootstrap %s: %v", addr, err)
			continue
		}

		c := Contact{IP: ip, Port: port}

		// PING to verify connectivity
		msg := Message{Type: MsgPing, Sender: n.contact}
		resp, err := n.sendAndWait(c, msg, 10*time.Second)
		if err != nil {
			log.Printf("[dht] Bootstrap %s unreachable: %v", addr, err)
			continue
		}
		if resp.Type == MsgPong {
			if resp.Sender.ID != [20]byte{} {
				n.routingTable.Add(resp.Sender)
			}
			log.Printf("[dht] Bootstrap %s responded", addr)
		}

		// FIND_NODE for our own ID to discover neighbors
		findMsg := Message{
			Type:     MsgFindNode,
			Sender:   n.contact,
			TargetID: n.id,
		}
		findResp, err := n.sendAndWait(c, findMsg, 10*time.Second)
		if err == nil && len(findResp.Contacts) > 0 {
			for _, nc := range findResp.Contacts {
				n.routingTable.Add(nc)
			}
			log.Printf("[dht] Discovered %d nodes via bootstrap", len(findResp.Contacts))
		}
	}
	log.Printf("[dht] Bootstrap complete. Routing table size: %d", n.routingTable.Size())
}

func (n *Node) handleMessage(data []byte, addr *net.UDPAddr) {
	var msg Message
	if err := json.Unmarshal(data, &msg); err != nil {
		return
	}

	switch msg.Type {
	case MsgPing:
		resp := Message{Type: MsgPong, Sender: n.contact}
		n.sendBytes(resp, addr)

		if msg.Sender.ID != [20]byte{} {
			n.routingTable.Add(msg.Sender)
		}

	case MsgPong:
		if msg.Sender.ID != [20]byte{} {
			n.routingTable.Add(msg.Sender)
		}

	case MsgFindNode:
		closest := n.routingTable.FindClosest(msg.TargetID, 20)
		resp := Message{
			Type:     MsgFindNodeR,
			Sender:   n.contact,
			Contacts: closest,
		}
		n.sendBytes(resp, addr)
		if msg.Sender.ID != [20]byte{} {
			n.routingTable.Add(msg.Sender)
		}

	case MsgFindNodeR:
		for _, c := range msg.Contacts {
			n.routingTable.Add(c)
		}

	case MsgStore:
		n.storeMu.Lock()
		n.store[msg.Key] = StoredValue{
			Key:       msg.Key,
			Value:     msg.Value,
			ExpiresAt: time.Now().Add(24 * time.Hour).Unix(),
		}
		n.storeMu.Unlock()
		resp := Message{Type: MsgStoreR, Sender: n.contact, Key: msg.Key}
		n.sendBytes(resp, addr)

	case MsgFindValue:
		n.storeMu.RLock()
		sv, ok := n.store[msg.Key]
		n.storeMu.RUnlock()

		var resp Message
		if ok && sv.ExpiresAt > time.Now().Unix() {
			resp = Message{Type: MsgFindValueR, Sender: n.contact, Key: msg.Key, Value: sv.Value}
		} else {
			resp = Message{Type: MsgFindValueR, Sender: n.contact, Key: msg.Key}
		}
		n.sendBytes(resp, addr)
	}
}

func (n *Node) sendMessage(c Contact, msg Message) {
	data, err := json.Marshal(msg)
	if err != nil {
		return
	}
	addr := &net.UDPAddr{IP: c.IP, Port: c.Port}
	n.conn.WriteToUDP(data, addr)
}

func (n *Node) sendBytes(msg Message, addr *net.UDPAddr) {
	data, err := json.Marshal(msg)
	if err != nil {
		return
	}
	n.conn.WriteToUDP(data, addr)
}

func (n *Node) sendAndWait(c Contact, msg Message, timeout time.Duration) (*Message, error) {
	data, err := json.Marshal(msg)
	if err != nil {
		return nil, err
	}

	addr := &net.UDPAddr{IP: c.IP, Port: c.Port}
	if _, err := n.conn.WriteToUDP(data, addr); err != nil {
		return nil, err
	}

	// Wait for response by temporarily reading
	n.conn.SetReadDeadline(time.Now().Add(timeout))
	buf := make([]byte, 8192)
	for {
		nbytes, raddr, err := n.conn.ReadFromUDP(buf)
		if err != nil {
			n.conn.SetReadDeadline(time.Time{})
			return nil, err
		}
		if !raddr.IP.Equal(c.IP) || raddr.Port != c.Port {
			continue
		}
		var resp Message
		if err := json.Unmarshal(buf[:nbytes], &resp); err != nil {
			continue
		}
		n.conn.SetReadDeadline(time.Time{})
		return &resp, nil
	}
}

// Helper functions

func generateNodeID() NodeID {
	var id NodeID
	// Use hostname + random for uniqueness
	rand.Read(id[:])
	return id
}

func sha1Hash(data []byte) NodeID {
	h := sha1.Sum(data)
	return NodeID(h)
}

func getOutboundIP() net.IP {
	conn, err := net.Dial("udp", "8.8.8.8:80")
	if err != nil {
		return net.IPv4(127, 0, 0, 1)
	}
	defer conn.Close()
	localAddr := conn.LocalAddr().(*net.UDPAddr)
	return localAddr.IP
}

func resolveAddr(addr string) (net.IP, int, error) {
	host, portStr, err := net.SplitHostPort(addr)
	if err != nil {
		host = addr
		portStr = "66881"
	}
	port := 66881
	if p := atoi(portStr); p > 0 {
		port = p
	}

	ips, err := net.LookupIP(host)
	if err != nil || len(ips) == 0 {
		return nil, 0, fmt.Errorf("resolve %s: %w", host, err)
	}
	return ips[0], port, nil
}

func atoi(s string) int {
	var n int
	for _, c := range s {
		if c < '0' || c > '9' {
			return 0
		}
		n = n*10 + int(c-'0')
	}
	return n
}
