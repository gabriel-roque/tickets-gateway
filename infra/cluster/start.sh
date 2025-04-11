kind create cluster --name tickets-gateway-cluster --config tickets-gateway-cluster.yaml

kubectl apply -f tickets-gateway-deployment.yaml

kubectl port-forward svc/tickets-api-service 5000:80

